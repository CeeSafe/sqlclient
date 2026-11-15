#!/bin/sh
# Runs the package's tests: offline first, then live against SQL Server and Azure SQL, as the environment allows.
#
#   MSSQL_TEST_CONNECTION    a SQL Server connection string with a SQL login, e.g.
#                            'Server=host,1433;Database=sqlclient_test;User Id=sa;Password=…;TrustServerCertificate=true'
#   MSSQL_AZURE_CONNECTION   an Azure SQL connection string for Entra ID (no password), e.g.
#                            'Server=tcp:myserver.database.windows.net,1433;Database=sqlclient_test'; the access token
#                            comes from `az account get-access-token` (az installed and logged in)
#   MSSQL_TEST_BACKUP_PATH   also the backup test against SQL Server, which writes that file on the server
#   CEESAFEC                 the compiler: default tools/ceesafec-0.47.1/ceesafec (tools/build-ceesafec.sh makes it),
#                            else ceesafec on PATH
#
# Credentials stay in the environment: never write them, or a token, into a file.
set -eu
cd "$(dirname "$0")/.."

if [ -z "${CEESAFEC:-}" ]; then
    if [ -x tools/ceesafec-0.47.1/ceesafec ]; then
        CEESAFEC=tools/ceesafec-0.47.1/ceesafec
    else
        CEESAFEC=ceesafec
    fi
fi
SQLSERVER=${MSSQL_TEST_CONNECTION:-}
AZURE=${MSSQL_AZURE_CONNECTION:-}

echo "== offline ($("$CEESAFEC" --version))"
env -u MSSQL_TEST_CONNECTION -u MSSQL_TEST_ACCESS_TOKEN -u MSSQL_TEST_BACKUP_PATH "$CEESAFEC" test | tail -1

if [ -n "$SQLSERVER" ]; then
    echo "== SQL Server"
    env -u MSSQL_TEST_ACCESS_TOKEN MSSQL_TEST_CONNECTION="$SQLSERVER" "$CEESAFEC" test | grep -v ' ok$'
fi

if [ -n "$AZURE" ]; then
    echo "== Azure SQL (Entra ID)"
    token=$(az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv)
    env -u MSSQL_TEST_BACKUP_PATH MSSQL_TEST_CONNECTION="$AZURE" MSSQL_TEST_ACCESS_TOKEN="$token" "$CEESAFEC" test \
        | grep -v ' ok$'
fi

if [ -z "$SQLSERVER" ] && [ -z "$AZURE" ]; then
    echo "(set MSSQL_TEST_CONNECTION and/or MSSQL_AZURE_CONNECTION to run the live tests)"
fi
