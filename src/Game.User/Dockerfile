# ---- 编译阶段 ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

RUN apt-get update && apt-get install -y clang zlib1g-dev && rm -rf /var/lib/apt/lists/*

COPY src/Game.Shared/Game.Shared.csproj Game.Shared/
COPY src/Game.User/Game.User.csproj Game.User/
RUN dotnet restore Game.User/Game.User.csproj

COPY src/Game.Shared/ Game.Shared/
COPY src/Game.User/ Game.User/
WORKDIR /src/Game.User
RUN dotnet publish Game.User.csproj -c Release -r linux-x64 -o /app/publish /p:PublishAot=true

# ---- 运行阶段 ----
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["./Game.User"]
