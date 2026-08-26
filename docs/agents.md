# Project Instructions

## Repository setup

Clone the repository with its dependencies, or initialize them after cloning:

```bash
git clone --recurse-submodules <repository-url>
# Or, in an existing clone:
git submodule update --init --recursive
```

The mod uses these submodules:

- `External/Adk`: supplies the ADK Roslyn analyzer used by the mod project.
- `External/ClientApi`: supplies the SE Procedural Cubemap client API as shared compiled source.

Do not edit vendored submodule source as part of ordinary changes to this repository. Make dependency changes in the dependency repository, then update the submodule commit here.

## Space Engineers source

Decompiled Space Engineers sources are stored in `{binarypath}/../SE.Source`, where `{binarypath}` is read from the `[mdk]` `binarypath=` entry in `ProceduralCubemapApi/mdk.local.ini`.

For repository-local access, run `./DumpSource.sh`. It creates or updates `./SE.Source` as a symlink when that path is absent or is already a symlink, then decompiles the managed assemblies with `ilspycmd`.

Before running the script:

1. Install `ilspycmd` and ensure it is available on `PATH`.
2. Ensure `ProceduralCubemapApi/mdk.local.ini` exists.
3. Replace `binarypath=auto` with the absolute path to the Space Engineers `Bin64` directory.

`ProceduralCubemapApi/mdk.local.ini` and `SE.Source` are machine-local and must not be committed.

## Space Engineers API usage

- This is an MDK mod project, not a programmable-block project. Use the full mod API and mod lifecycle components such as `MySessionComponentBase` where appropriate.
- Avoid importing the entire `Sandbox.ModAPI.Ingame` namespace unless necessary.
- Prefer `Sandbox.ModAPI` types over `Sandbox.ModAPI.Ingame` types when both are suitable.

## Local repository utilities

- `DumpSource.sh` is the shared source-dump helper.
- `compress-repository.local.sh` creates a local ZIP containing tracked and non-ignored project files. Its `.local.sh` suffix intentionally keeps it out of Git.
- Generated ZIP archives, IDE state, local MDK configuration, decompiled sources, and other `*.local.*` helpers are local-only.

## Deferred planet pushes

`ModificationTemplate.GetPushAction()` freezes a template and returns only the worker-safe preparation phase. Clients may execute many returned actions with `MyAPIGateway.Parallel.Do`. After all actions return, each template must receive exactly one `CompletePush` call on the originating simulation/loading thread; that method registers definitions, commits engine entity state, and invokes its callback synchronously. Do not move definition registration or planet storage commits into the returned worker action.
