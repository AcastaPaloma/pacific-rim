#!/bin/bash
cd "$(dirname "$0")"
for i in 1 2 3 4; do ./run.sh C $i > C_$i.out 2>&1; done
echo done > loop.done
