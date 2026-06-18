# Multi-stage build for the stashr server (ADR-0014).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Stashr.Server/Stashr.Server.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Run as a non-root user.
RUN useradd --uid 5678 --create-home stashr && chown -R stashr /app
USER stashr

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "Stashr.Server.dll"]
