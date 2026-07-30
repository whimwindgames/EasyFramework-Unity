#!/usr/bin/env bash
set -euo pipefail

project_path="${1:-$(pwd)}"
results_path="${2:-${project_path}/TestResults}"
unity_editor="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/6000.3.11f1/Unity.app/Contents/MacOS/Unity}"

if [[ ! -x "${unity_editor}" ]]; then
  echo "Unity Editor not found. Set UNITY_EDITOR to the Unity executable." >&2
  exit 1
fi

mkdir -p "${results_path}"

"${unity_editor}" \
  -batchmode \
  -projectPath "${project_path}" \
  -runTests \
  -testPlatform editmode \
  -testResults "${results_path}/editmode.xml" \
  -logFile "${results_path}/editmode.log"

"${unity_editor}" \
  -batchmode \
  -projectPath "${project_path}" \
  -runTests \
  -testPlatform playmode \
  -testResults "${results_path}/playmode.xml" \
  -logFile "${results_path}/playmode.log"

echo "Unity tests passed. Results: ${results_path}"
