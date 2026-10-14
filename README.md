# Therapy notes

Windows application for recording sessions, local Russian transcription, PII redaction and structured reports.

WPF on .NET 9, NAudio, SQLite, GigaAM, Natasha and Supabase. Audio processing runs locally. Report and chat requests use configured cloud services.

## Build

Requires Windows, the .NET 9 SDK and Python 3.11+.

```powershell
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
dotnet build AudioRecorderTrayApp.csproj -c Release
```

Set `SUPABASE_URL` and `SUPABASE_ANON_KEY` before starting the application. Set `GEMINI_API_KEY` to enable text enhancement. `UPDATE_FEED_URL` is optional; updates are skipped when it is unset.

The Supabase tables, authentication configuration and Edge Functions must be deployed separately. Their server implementation is not included here.

Use the Python path setting to select your local environment. Model files download on first use and are excluded from Git.

## Code

- `Audio/`: microphone and system-audio capture.
- `Services/`: transcription, storage, redaction and report processing.
- `Core/`: application settings, encryption and service integration.
- `Windows/`: WPF views.
- `Models/`: stored domain entities.
- `gigaam_server.py`, `natasha_redact.py`, `enhancer.py`: Python processing.

Recordings, transcripts, mappings and encryption keys are runtime data. No session data or configured credentials are included.
