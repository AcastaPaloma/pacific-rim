#!/bin/bash
until ! ps ax -o command | grep -q "[U]nity.app/Contents/MacOS/Unity -batchmode"; do sleep 1; done
m=$(memory_pressure | tail -1 | grep -o '[0-9]*%' | tr -d %); [ "${m:-0}" -lt 30 ] && { echo "low memory ${m}%"; exit 9; }
exec "$@"
