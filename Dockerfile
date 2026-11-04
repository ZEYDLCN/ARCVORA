# ARCVORA: tek imaj, tek süreç (API + derlenmiş arayüz + uygulama içi worker). Harici gereksinim: PostgreSQL (DATABASE_URL).
# Yerelde: docker build -t arcvora . && docker run -p 8080:8080 -e DATABASE_URL=postgres://... -e Jwt__Key=<en az 32 karakter> arcvora
# ---- 1) arayüz
FROM node:22-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

# ---- 2) API yayını
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS api
WORKDIR /src
COPY Directory.Build.props ./
COPY src/ src/
RUN dotnet publish src/Arcvora.Api -c Release -o /out --nologo

# ---- 3) çalışma imajı
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=api /out ./
COPY --from=web /web/dist ./wwwroot

# Üretim varsayılanları (Jwt__Key ve DATABASE_URL ZORUNLU, ortamdan verilir; yoksa uygulama başlamaz).
ENV ASPNETCORE_ENVIRONMENT=Production \
    Worker__InProcess=true \
    Database__MigrateOnStartup=true \
    ForwardedHeaders__Enabled=true \
    DOTNET_gcServer=0

USER app
EXPOSE 8080
# Barındırıcılar (Render, Koyeb, Fly…) PORT verir; yoksa 8080.
CMD ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080} exec dotnet Arcvora.Api.dll"]
