## Summary

<!-- What changed and why. -->

## Checks

- [ ] No secrets, Windows profile paths, machine names, or device ids
- [ ] User-facing strings updated in both `en-US` and `fr-FR` when the UI changed
- [ ] `dotnet test Mixdeck.slnx -c Release` (or CI) is green

## Notes

UI tests (`MIXDECK_UI_TESTS=1`) are local only. They are not part of GitHub Actions.
