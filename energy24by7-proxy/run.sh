#!/usr/bin/with-contenv bashio

export E24_EMAIL=$(bashio::config 'email')
export E24_PASSWORD=$(bashio::config 'password')
export E24_DEVICE_ID=$(bashio::config 'device_id')
export FETCH_INTERVAL=$(bashio::config 'fetch_interval')

exec /app/energy24by7-proxy
