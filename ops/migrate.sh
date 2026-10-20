#!/bin/sh
set -eu

./efbundle --connection "$ConnectionStrings__Postgres"
./seed/OrderService.Migrator --seed-only

