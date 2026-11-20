#!/bin/sh
# commits-since <hours> [prefix...] - a MagnaFlow action condition.
# Exit 0 when HEAD has a commit from the last <hours> hours whose subject starts with none of
# the prefixes (literal, case-sensitive), 1 when it has none, 2 on bad usage or a git failure.
# One stdout line, e.g. "3 commits in the last 16h (2 ignored)".

usage() {
    echo "usage: commits-since <hours> [prefix...] (hours: a positive integer)"
    exit 2
}

hours=$1
case "$hours" in
    '' | *[!0-9]*) usage ;;
esac
[ "$hours" -gt 0 ] 2>/dev/null || usage
shift

subjects=$(git log --since="$hours hours ago" --format=%s HEAD) || {
    echo "commits-since: git log failed"
    exit 2
}

total=0
kept=0
if [ -n "$subjects" ]; then
    while IFS= read -r subject; do
        total=$((total + 1))
        ignored=no
        for prefix in "$@"; do
            case "$subject" in
                "$prefix"*) ignored=yes; break ;;
            esac
        done
        [ "$ignored" = no ] && kept=$((kept + 1))
    done <<SUBJECTS
$subjects
SUBJECTS
fi

skipped=$((total - kept))
if [ "$kept" -eq 0 ]; then
    echo "no commits in the last ${hours}h ($skipped ignored)"
    exit 1
fi
[ "$kept" -eq 1 ] && noun=commit || noun=commits
echo "$kept $noun in the last ${hours}h ($skipped ignored)"
exit 0
