# Giai đoạn Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release

# Cài đặt các thư viện cần thiết cho Native AOT trên Linux
RUN apt-get update && apt-get install -y --no-install-recommends \
    clang zlib1g-dev

WORKDIR /src

# Copy các project file
COPY ["Services/Dragon-Business/Dragon.Business.csproj", "Services/Dragon-Business/"]
COPY ["Services/RedisFlow/src/RedisFlow/RedisFlow.csproj", "Services/RedisFlow/src/RedisFlow/"]

# Restore
RUN dotnet restore "Services/Dragon-Business/Dragon.Business.csproj"

# Copy toàn bộ source code
COPY . .

# Build Native AOT
WORKDIR "/src/Services/Dragon-Business"
RUN dotnet publish "Dragon.Business.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:PublishAot=true

# Giai đoạn Final (siêu nhẹ)
FROM mcr.microsoft.com/dotnet/nightly/runtime-deps:9.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app/publish .

# Tạo thư mục Data cho SQLite
USER root
RUN mkdir -p /app/Data && chown -R 1654:1654 /app/Data
USER 1654

ENTRYPOINT ["./Dragon.Business"]
