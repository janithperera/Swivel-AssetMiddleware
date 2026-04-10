FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/AssetMiddleware.Api/AssetMiddleware.Api.csproj", "src/AssetMiddleware.Api/"]
COPY ["src/AssetMiddleware.Application/AssetMiddleware.Application.csproj", "src/AssetMiddleware.Application/"]
COPY ["src/AssetMiddleware.Domain/AssetMiddleware.Domain.csproj", "src/AssetMiddleware.Domain/"]
COPY ["src/AssetMiddleware.Infrastructure/AssetMiddleware.Infrastructure.csproj", "src/AssetMiddleware.Infrastructure/"]

RUN dotnet restore "src/AssetMiddleware.Api/AssetMiddleware.Api.csproj"

COPY . .

RUN dotnet publish "src/AssetMiddleware.Api/AssetMiddleware.Api.csproj" \
    -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .

# Run as non-root user
RUN adduser --disabled-password --gecos "" appuser && chown -R appuser /app
USER appuser

ENTRYPOINT ["dotnet", "AssetMiddleware.Api.dll"]
