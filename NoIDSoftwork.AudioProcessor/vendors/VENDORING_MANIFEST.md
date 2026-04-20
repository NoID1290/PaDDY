# Vendoring Manifest

This folder contains source-vendored dependencies compiled directly into `NoIDSoftwork.AudioProcessor.dll`.

## Policy

- Managed dependencies are vendored as source and compiled into the project.
- `PackageReference` entries were removed from `NoIDSoftwork.AudioProcessor.csproj`.
- Native MP3 runtime binaries are intentionally kept external:
  - `vendors/NAudio.Lame/NAudio.Lame/libmp3lame.32.dll`
  - `vendors/NAudio.Lame/NAudio.Lame/libmp3lame.64.dll`

## Imported Sources

| Dependency | Upstream Repository | Version Source | Pinned Ref |
|---|---|---|---|
| NAudio | https://github.com/naudio/NAudio | NuGet `NAudio` 2.3.0 | `24c0394da0cffeddc70a13f6c6d5f55f4af0dea2` (tag `v2.3.0`) |
| NAudio.Lame | https://github.com/Corey-M/NAudio.Lame | NuGet `NAudio.Lame` 2.1.0 | `5b5bb009d8c63ef8ab95cb36ca9105c8acc1c5fc` (tag `v2.1.0`) |
| NAudio.Vorbis | https://github.com/naudio/Vorbis | NuGet `NAudio.Vorbis` 1.5.0 | `adb0443f6a3e87e29fdd0e592efa57396f014832` (tag `v1.5.0`) |
| NVorbis | https://github.com/NVorbis/NVorbis | NuGet transitive via `NAudio.Vorbis` 0.10.4 | `871138f37dd72c8d908aa6360a506337a437504d` (tag `v0.10.4`) |
| Concentus | https://github.com/lostromb/concentus | NuGet `Concentus` 2.2.2 | `6c2328dc19044601e33a9c11628b8d60e1f3011c` (from nuspec) |
| Concentus.OggFile | https://github.com/lostromb/concentus.oggfile | NuGet `Concentus.OggFile` 1.0.7 | `4193744a52506faa48e34ba7b510be9e7f32bfbd` (repo HEAD at import) |
| OggVorbisEncoder | https://github.com/SteveLillis/.NET-Ogg-Vorbis-Encoder | NuGet `OggVorbisEncoder` 1.2.2 | `9211018f92f09cff58bb0e98e3af322e83c48f3c` (repo HEAD at import) |

## Licensing

License files are copied into `vendors/licenses/` for attribution and compliance.

## Notes

- Two nuspec commit references were not reachable from shallow tag checkout during import (`NAudio`, `Concentus.OggFile`). The vendored refs above are the exact imported snapshots.
- `.git` metadata from imported repositories has been removed so vendored code is tracked as normal source in this repository.
