# API imajı (docs/canli-ortam.md): GitHub Actions'ta derlenir, sunucuya `docker save | ssh docker load` ile aktarılır.
# İçinde EF Core migration bundle'ı da var (/app/efbundle): dağıtımda yedekten hemen sonra aynı imajla çalıştırılır.
# Bağlam: backend/ klasörü.
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props KobiFabrika.slnx ./
COPY src/KobiFabrika.Domain/KobiFabrika.Domain.csproj src/KobiFabrika.Domain/
COPY src/KobiFabrika.Infrastructure/KobiFabrika.Infrastructure.csproj src/KobiFabrika.Infrastructure/
COPY src/KobiFabrika.Api/KobiFabrika.Api.csproj src/KobiFabrika.Api/
RUN dotnet restore src/KobiFabrika.Api/KobiFabrika.Api.csproj -r linux-x64
COPY src/ src/
RUN dotnet publish src/KobiFabrika.Api/KobiFabrika.Api.csproj -c Release -r linux-x64 --self-contained false --no-restore -o /out/api
# Bundle derlenirken uygulama ayağa kalkmaz ama bağlantı dizesi olmadan servis kaydı hata verir: sahte değer.
RUN dotnet tool install --global dotnet-ef --version "10.*" \
 && ConnectionStrings__Default="Server=bundle;Database=bundle" /root/.dotnet/tools/dotnet-ef migrations bundle \
    -p src/KobiFabrika.Infrastructure -s src/KobiFabrika.Api -r linux-x64 -o /out/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS run
RUN apt-get update && apt-get install -y --no-install-recommends curl tzdata && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /out/api ./
COPY --from=build /out/efbundle ./efbundle
# Kalıcı birim (Data Protection anahtarları, logo dosyaları, kayıtlar) bu klasörlere bağlanır; sahibi uygulama kullanıcısı.
RUN mkdir -p /app/App_Data /app/logs && chown -R app:app /app/App_Data /app/logs
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0 \
    TZ=UTC
USER app
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
  CMD curl -fsS -o /dev/null http://127.0.0.1:8080/health || exit 1
ENTRYPOINT ["dotnet", "KobiFabrika.Api.dll"]
