# Extracts the changelog section for a given version from CHANGELOG.md.
# Usage: get-release-notes.sh <version> <changelog-file>
set -e

VERSION="$1"
FILE="$2"

awk -v ver="$VERSION" '
  $0 ~ /^## \[/ {
    line = $0
    gsub(/^## \[/, "", line)
    gsub(/\] - .*$/, "", line)
    if (line == ver) { in_section = 1; next }
    if (in_section && line != "Unreleased") { exit }
  }
  in_section && $0 !~ /^## \[/ { print }
' "$FILE" | sed '/^[[:space:]]*$/d'
