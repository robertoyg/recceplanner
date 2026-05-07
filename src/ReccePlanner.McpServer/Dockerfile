FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 5000

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore — copy project files first for layer caching
COPY ["src/ReccePlanner.Core/ReccePlanner.Core.csproj", "src/ReccePlanner.Core/"]
COPY ["src/ReccePlanner.McpServer/ReccePlanner.McpServer.csproj", "src/ReccePlanner.McpServer/"]
RUN dotnet restore "src/ReccePlanner.McpServer/ReccePlanner.McpServer.csproj"

# Copy source and publish
COPY ["src/ReccePlanner.Core/", "src/ReccePlanner.Core/"]
COPY ["src/ReccePlanner.McpServer/", "src/ReccePlanner.McpServer/"]
RUN dotnet publish "src/ReccePlanner.McpServer/ReccePlanner.McpServer.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:5000
ENTRYPOINT ["dotnet", "ReccePlanner.McpServer.dll"]
