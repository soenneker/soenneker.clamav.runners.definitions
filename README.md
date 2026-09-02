# Soenneker.Clamav.Runners.Definitions

Automation for maintaining `Soenneker.Clamav.Definitions` from the official ClamAV database service.

The runner uses `Soenneker.Clamav.Freshclam.Util`, starts from the most recently published definition seed when available, validates all three official databases, removes machine-specific FreshClam state, and publishes only when the database content changes.

The runner source is MIT-licensed. Generated definition packages preserve upstream licensing and provenance.
