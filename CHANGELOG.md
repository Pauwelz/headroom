# Changelog

All notable changes to this project are in this file.
The format follows [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/).
This project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Each GitHub Release carries the build provenance of its image as `headroom-X.Y.Z.sigstore.json`.

### Security

- The consent page encodes hidden-field names. It forwards only known OAuth parameters.
- The login, first-run, consent and help pages send a Content-Security-Policy that blocks script and framing.

## [0.1.0] - 2026-11-06

### Added

- First public release.
