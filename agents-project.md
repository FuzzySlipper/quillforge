# QuillForge Local Bootstrap

Project-specific live guidance lives in Den at `[doc: quillforge/project-bootstrap-guide]`.

Use project ID `quillforge` for Den tasks, messages, documents, librarian queries, and guidance lookups.

## Local Commands

```bash
dotnet restore QuillForge.slnx
dotnet build QuillForge.slnx -p:AllowMissingPrunePackageData=true
dotnet test QuillForge.slnx -p:AllowMissingPrunePackageData=true
```

For synthetic/manual/live build tests, follow `docs/synthetic-testing.md`.
