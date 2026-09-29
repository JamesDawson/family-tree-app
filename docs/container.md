# Container image

`FamilyTree.Web` builds into a container image with the .NET SDK container support. No Dockerfile is needed.

## Build

```powershell
dotnet publish src/FamilyTree.Web -t:PublishContainer -p:ContainerImageTags=1.0.0
```

The image is `family-tree-web:<tag>`. It uses the `aspnet:10.0-noble-chiseled` base (non-root, no shell), ReadyToRun, and `linux-x64`. For arm64, add `-p:ContainerRuntimeIdentifier=linux-arm64`.

The app is not trimmed or AOT-compiled, because LibGit2Sharp is native and YamlDotNet uses reflection.

## Run

The app stores its data as a git repository at `/data`. Mount a volume there and set the commit author:

```powershell
docker volume create family-tree-data
# One-time: the image runs as uid 1654, so give it the volume
docker run --rm -v family-tree-data:/data busybox chown 1654:1654 /data

docker run -d -p 8080:8080 -v family-tree-data:/data `
  -e FamilyTreeData__DefaultCommitAuthorName="Your Name" `
  -e FamilyTreeData__DefaultCommitAuthorEmail="you@example.com" `
  family-tree-web:1.0.0
```

Then open <http://localhost:8080>. The app starts only if both author settings are set.

The image serves HTTP only. The "Failed to determine the https port" warning in the logs is expected. Put a TLS-terminating proxy in front of it if needed.
