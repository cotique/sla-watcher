# Build and run the worker as a pod. Exists for the failure tests: a container can be
# OOM-killed by the kernel, paused mid-execution, or cut off from the database, and none of
# those can be done to a process on the host.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY SlaWatcher.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /app
COPY --from=build /app .

# Without this the runtime sizes its heap from the cgroup limit and answers a large
# allocation with OutOfMemoryException, which the process survives. The tests need the
# kernel to kill it instead, so the allocation goes to native memory and is touched.
ENV DOTNET_gcServer=0

ENTRYPOINT ["dotnet", "SlaWatcher.dll"]
