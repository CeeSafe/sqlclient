#!/bin/sh
# Builds ceesafec from a tag of the CeeSafe repository into tools/ceesafec-<version>/ceesafec, with the ceesafec on
# PATH (any release from 0.8.0 builds it). For when the installed compiler is older than ceesafe.toml asks for.
#
#   tools/build-ceesafec.sh [version] [CeeSafe checkout]     defaults: 0.47.1, ~/dev/CeeSafe
#
# The sources are exported from the tag (git archive), so the checkout's branch and working tree are left alone.
set -eu
version=${1:-0.47.1}
checkout=${2:-$HOME/dev/CeeSafe}
cd "$(dirname "$0")"
target=$(pwd)/ceesafec-$version

rm -rf "$target/src"
mkdir -p "$target/src"
git -C "$checkout" archive "v$version" | tar -x -C "$target/src"
(cd "$target/src" && mkdir -p out && ceesafec build -o "$target/ceesafec")
rm -rf "$target/src"
"$target/ceesafec" --version
