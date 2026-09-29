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

## Syncing the data with GitHub

To keep the data in a GitHub repository, also set:

| Variable | Purpose |
| --- | --- |
| `FamilyTreeData__RemoteUrl` | HTTPS URL of the data repo, e.g. `https://github.com/<owner>/<repo>.git` |
| `FamilyTreeData__GitHubToken` | Personal access token with read and write access to that repo's contents |

With `docker compose`, set `DATA_REPO_URL` and `GITHUB_PAT` in `.env` instead.

- **First run:** an empty `/data` volume is initialised by cloning the repo. An empty remote is seeded with a baseline commit.
- **Later runs:** if `/data` already holds a clone, the app fast-forwards it from the remote at startup. If that fails (offline, diverged), it logs a warning and starts with the local copy. `RemoteUrl` is not re-cloned.
- **Every change:** the app commits and then pushes. If a push fails, the commit stays local, a warning is logged, and the next successful push includes it.
- **A failed first clone** stops the app at startup with an error. Check the URL and the token.
- The token is not written to `/data/.git/config`. It is visible in the container's environment (`docker inspect`), so prefer a fine-grained token limited to one repo.

The volume ownership step above is still needed.

## Notes

The image serves HTTP only. The "Failed to determine the https port" warning in the logs is expected. Put a TLS-terminating proxy in front of it if needed.
