# .deckhand/Dockerfile: KCOM's toolchain on top of Deckhand's base image.
ARG DECKHAND_BASE=deckhand-sandbox
FROM $DECKHAND_BASE
USER root

# luacheck (UPGRADE_PLAN.md W13), from Debian's packages.
RUN apt-get update && apt-get install -y --no-install-recommends lua-check \
    && rm -rf /var/lib/apt/lists/*

# The .NET SDK (10.0 LTS) and pwsh 7.6 LTS for the plan's PowerShell scripts (W9).
# pwsh 7.7.0's tool package doesn't install, hence the pin.
ARG DOTNET_CHANNEL=10.0
ENV DOTNET_ROOT=/usr/share/dotnet DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    PATH=/usr/share/dotnet:/opt/dotnet-tools:$PATH
RUN curl -fsSL https://dot.net/v1/dotnet-install.sh \
      | bash -s -- --channel "$DOTNET_CHANNEL" --install-dir "$DOTNET_ROOT" \
    && dotnet tool install PowerShell --version '7.6.*' --tool-path /opt/dotnet-tools \
    && rm -rf /root/.nuget /root/.dotnet

# The CopyDLLs targets read $(USERPROFILE)\.nuget\packages (until W3 is fixed).
ENV USERPROFILE=/home/node
USER node
