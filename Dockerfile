# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet tool restore
RUN dotnet restore OrderService.slnx --locked-mode
RUN dotnet publish src/OrderService.Api/OrderService.Api.csproj \
    --configuration Release --no-restore --output /out/api
RUN dotnet publish src/OrderService.Migrator/OrderService.Migrator.csproj \
    --configuration Release --no-restore --output /out/seed
RUN dotnet ef migrations bundle \
    --project src/OrderService.Infrastructure/OrderService.Infrastructure.csproj \
    --startup-project src/OrderService.Api/OrderService.Api.csproj \
    --configuration Release --no-build \
    --target-runtime linux-x64 \
    --output /out/migrator/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app
COPY --from=build /out/api .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "OrderService.Api.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS migrator
WORKDIR /app
COPY --from=build /out/migrator/efbundle ./efbundle
COPY --from=build /out/seed ./seed
COPY ops/migrate.sh ./migrate.sh
RUN chmod +x ./efbundle ./migrate.sh ./seed/OrderService.Migrator
USER $APP_UID
ENTRYPOINT ["./migrate.sh"]
