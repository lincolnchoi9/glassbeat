#!/bin/bash
# Turns Swift compiler errors/warnings and XCTest failures in a log into GitHub annotations, errors first (GitHub keeps 10 of
# each per step). Matches "/path/File.swift:12:5: error: msg" and "/path/File.swift:12: error: -[Suite test] : msg".
sed -nE 's#^(/[^:]+\.swift):([0-9]+):([0-9]+:)? (error|warning): (.*)$#::\4 file=\1,line=\2::\5#p' "$1" \
  | sed "s#file=${GITHUB_WORKSPACE:-/nonexistent}/#file=#" | sort -u | head -n 18
