#!/usr/bin/env python3
"""sqlclient (CeeSafe) against Microsoft.Data.SqlClient (.NET), scenario by scenario, on one server.

usage: [CEESAFEC=path/to/ceesafec] MSSQL_CONNECTION='Server=…;Database=…;User Id=…;Password=…;TrustServerCertificate=true' \
           python3 benchmarks/run.py [--rounds 3] [--only select1,rows] [--out benchmarks/results]

Each scenario runs in a process of its own, for each client, `--rounds` times, the clients alternating which goes
first in each round. A process does untimed warm-up iterations, then timed ones, and prints a nanosecond sample per
iteration and a checksum of what it read; both clients must agree on the checksum. The table gives the median over
every sample of every round, the 95th percentile, the CPU time (user + system, all threads) of a timed iteration,
measured in the process around the timed loop so start-up and JIT are left out, the whole process's CPU time, and
its peak resident memory, from `/usr/bin/time -l` (macOS) or `-v` (Linux).
"""
import argparse
import json
import os
import platform
import re
import statistics
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))

# name: (warm-up iterations, timed iterations, what one iteration is)
SCENARIOS = {
    "connect": (2, 10, "open (TCP, TLS, login) and close, no pool"),
    "select1": (5, 40, "SELECT 1 on a warm connection"),
    "param": (5, 40, "a parameterized single-row lookup by primary key"),
    "rows": (1, 5, "read 100,000 rows of six types, every value"),
    "insert": (1, 3, "100 parameterized INSERTs in a transaction"),
    "batch": (1, 5, "the same 100 INSERTs as one batch (SqlBatch)"),
    "bigvalue": (2, 10, "a 1 MB nvarchar(max) parameter echoed back"),
    "json": (2, 10, "FOR JSON of 10,000 rows, joined"),
    "pool": (1, 5, "16 tasks at once x 25 SELECT 1 through a pool of 16"),
}


def build():
    ceesafe = os.path.join(HERE, "ceesafe")
    subprocess.run([os.environ.get("CEESAFEC", "ceesafec"), "build"], cwd=ceesafe, check=True, stdout=subprocess.DEVNULL)
    dotnet = os.path.join(HERE, "dotnet")
    subprocess.run(["dotnet", "build", "-c", "Release", "--nologo", "-v", "q"], cwd=dotnet, check=True,
                   stdout=subprocess.DEVNULL)
    return {
        "sqlclient": [os.path.join(ceesafe, "out", "bench")],
        "Microsoft.Data.SqlClient": ["dotnet", os.path.join(dotnet, "bin", "Release", "net10.0", "Bench.dll")],
    }


def timed(command):
    """Runs `command` under /usr/bin/time; gives its stdout, CPU seconds (user + system) and peak RSS in bytes."""
    mac = platform.system() == "Darwin"
    wrapper = ["/usr/bin/time", "-l" if mac else "-v"]
    done = subprocess.run(wrapper + command, capture_output=True, text=True)
    if done.returncode != 0:
        sys.exit(f"{' '.join(command)} failed:\n{done.stdout}\n{done.stderr}")
    err = done.stderr
    if mac:
        times = re.search(r"([\d.]+) real\s+([\d.]+) user\s+([\d.]+) sys", err)
        cpu = float(times.group(2)) + float(times.group(3))
        rss = int(re.search(r"(\d+)\s+maximum resident set size", err).group(1))
    else:
        user = float(re.search(r"User time \(seconds\): ([\d.]+)", err).group(1))
        system = float(re.search(r"System time \(seconds\): ([\d.]+)", err).group(1))
        cpu = user + system
        rss = int(re.search(r"Maximum resident set size \(kbytes\): (\d+)", err).group(1)) * 1024
    return done.stdout, cpu, rss


def parse(output):
    samples = [int(m.group(1)) for m in re.finditer(r"^sample (\d+)$", output, re.M)]
    check = re.search(r"^check (\S+)$", output, re.M)
    cpu = re.search(r"^cpu (\d+)$", output, re.M)
    return samples, (check.group(1) if check else None), (int(cpu.group(1)) if cpu else None)


def percentile(values, p):
    ordered = sorted(values)
    k = max(0, min(len(ordered) - 1, int(round(p / 100 * (len(ordered) - 1)))))
    return ordered[k]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--rounds", type=int, default=3)
    parser.add_argument("--only", default="")
    parser.add_argument("--out", default=os.path.join(HERE, "results"))
    args = parser.parse_args()
    if "MSSQL_CONNECTION" not in os.environ:
        sys.exit("set MSSQL_CONNECTION")
    names = [n for n in SCENARIOS if not args.only or n in args.only.split(",")]
    clients = build()
    setup, _, _ = timed(clients["sqlclient"] + ["setup", "0", "0"])
    print(f"setup: dbo.bench_rows has {parse(setup)[1]} rows", file=sys.stderr)

    results = {}
    for name in names:
        warmup, iterations, what = SCENARIOS[name]
        per = {c: {"samples": [], "cpu": [], "process_cpu": [], "rss": [], "checks": set()} for c in clients}
        for r in range(args.rounds):
            order = list(clients) if r % 2 == 0 else list(reversed(list(clients)))
            for client in order:
                out, cpu, rss = timed(clients[client] + [name, str(warmup), str(iterations)])
                samples, check, loop_cpu = parse(out)
                per[client]["samples"] += samples
                per[client]["cpu"].append(loop_cpu / iterations / 1e9)
                per[client]["process_cpu"].append(cpu)
                per[client]["rss"].append(rss)
                per[client]["checks"].add(check)
            print(f"{name}: round {r + 1} of {args.rounds}", file=sys.stderr)
        checks = set().union(*(per[c]["checks"] for c in clients))
        results[name] = {
            "what": what,
            "same_result": len(checks) == 1,
            "checks": sorted(str(c) for c in checks),
            "clients": {
                c: {
                    "median_ms": statistics.median(per[c]["samples"]) / 1e6,
                    "p95_ms": percentile(per[c]["samples"], 95) / 1e6,
                    "cpu_ms_per_iteration": statistics.median(per[c]["cpu"]) * 1000,
                    "process_cpu_s": statistics.median(per[c]["process_cpu"]),
                    "peak_rss_mb": max(per[c]["rss"]) / (1024 * 1024),
                    "samples": len(per[c]["samples"]),
                }
                for c in clients
            },
        }

    os.makedirs(args.out, exist_ok=True)
    with open(os.path.join(args.out, "results.json"), "w") as f:
        json.dump({"machine": f"{platform.system()} {platform.machine()}", "rounds": args.rounds,
                   "results": results}, f, indent=2)
    lines = ["| Scenario | sqlclient median | MDS median | sqlclient p95 | MDS p95 | sqlclient CPU/iter | MDS CPU/iter |"
             " CPU ratio | sqlclient process CPU | MDS process CPU | sqlclient RSS | MDS RSS | same result |",
             "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|"]
    for name, r in results.items():
        a, b = r["clients"]["sqlclient"], r["clients"]["Microsoft.Data.SqlClient"]
        lines.append(
            f"| `{name}`: {r['what']} | {a['median_ms']:.2f} ms | {b['median_ms']:.2f} ms | {a['p95_ms']:.2f} ms | "
            f"{b['p95_ms']:.2f} ms | {a['cpu_ms_per_iteration']:.2f} ms | {b['cpu_ms_per_iteration']:.2f} ms | "
            f"{b['cpu_ms_per_iteration'] / max(a['cpu_ms_per_iteration'], 1e-6):.1f}x | {a['process_cpu_s']:.2f} s | "
            f"{b['process_cpu_s']:.2f} s | {a['peak_rss_mb']:.0f} MB | {b['peak_rss_mb']:.0f} MB | "
            f"{'yes' if r['same_result'] else 'NO: ' + ', '.join(r['checks'])} |")
    table = "\n".join(lines)
    with open(os.path.join(args.out, "results.md"), "w") as f:
        f.write(table + "\n")
    print(table)


if __name__ == "__main__":
    main()
