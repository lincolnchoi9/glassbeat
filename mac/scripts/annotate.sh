#!/bin/bash
# Turns Swift compiler errors and XCTest failures in a log into GitHub ::error annotations (at most 9; GitHub keeps 10 per step).
# Matches "/path/File.swift:12:5: error: msg" and "/path/File.swift:12: error: -[Suite test] : msg".
sed -nE 's#^(/[^:]+\.swift):([0-9]+):([0-9]+:)? error: (.*)$#::error file=\1,line=\2::\4#p' "$1" \
  | sed "s#file=${GITHUB_WORKSPACE:-/nonexistent}/#file=#" | sort -u | head -n 9
