# IsoSwitch K6 Load Tests

This directory contains k6 load testing scripts for the IsoSwitch API.

## Requirements
- [k6](https://k6.io/docs/get-started/installation/) must be installed.

## Running the Tests

To run the load tests, you can execute the `isoswitch-load.js` script with k6. 
You can specify the test profile (smoke, soak, stress) via the `PROFILE` environment variable, and the base URL via `BASE_URL`.

### Smoke Test
Validates that the system works with minimal load.
```bash
k6 run -e PROFILE=smoke -e BASE_URL=http://localhost:5000 isoswitch-load.js
```

### Soak Test
Runs a moderate load over a longer period to check for memory leaks or degradation.
```bash
k6 run -e PROFILE=soak -e BASE_URL=http://localhost:5000 isoswitch-load.js
```

### Stress Test
Gradually increases load to find the breaking point of the system.
```bash
k6 run -e PROFILE=stress -e BASE_URL=http://localhost:5000 isoswitch-load.js
```

## Profiles

- `smoke`: 1 VUs for 10s
- `soak`: 10 VUs for 5m
- `stress`: Ramps up to 50 VUs over 2m, stays for 2m, ramps down for 1m.

## Metrics
The tests track HTTP request duration (95th and 99th percentiles) and request failure rates. The tests will fail if:
- HTTP request duration (p95) > 500ms
- Error rate > 1%
