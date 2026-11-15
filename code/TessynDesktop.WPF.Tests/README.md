# TessynDesktop.WPF.Tests

View-model, service and view-binding tests of the WPF client. They use `Fakes/FakeTessynClient` and never start the daemon or the app.

## Requirement: Node.js on PATH

`MediaGateScriptTests` runs `Web/media-gate.js` (the transcript page's remote-media gating) under **node** (`node -e ...`), because that
script is pure JavaScript shared with the page. On a machine or CI agent without `node` on PATH these tests **fail on purpose**, with a
message saying node could not be started, rather than passing without checking anything. Install Node.js (any current LTS) to run the suite.

## Other notes

- Tests that create WPF elements run on an STA thread (`Fakes/StaTestRunner`); no window is shown and no `Application` exists.
- `MarkdownNoRemoteResourceTests` parse the renderer's HTML with AngleSharp and assert the allowlist; they include the review's V1 input and a seeded fuzz.
- The end-to-end tests that start the built app live in `TessynDesktop.WPF.E2E.Tests` and are run explicitly (`dotnet test code/TessynDesktop.WPF.E2E.Tests`).
