# Start-button service checks

Run on Windows with the .NET 8 SDK:

```powershell
dotnet run --project tests/EDock.StartButtonTests/EDock.StartButtonTests.csproj -c Release
```

The standalone runner links the production service and substitutes a small dispatcher so it can check acknowledgements, image mapping publication, bounded resource ownership, and cleanup without launching EDock or connecting to Explorer. All 25 checks use generated temporary image files and process-owned mappings. The dummy helper file exists only in the test build output and is never loaded.
