# Giai đoạn Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release

# Cài đặt các thư viện cần thiết cho Native AOT trên Linux
RUN apt-get update && apt-get install -y --no-install-recommends \
    clang zlib1g-dev

WORKDIR /src

# Copy các project file
COPY ["Dragon.Business.csproj", "./"]
COPY ["External/RedisFlow/src/RedisFlow/RedisFlow.csproj", "External/RedisFlow/src/RedisFlow/"]

# Restore
RUN dotnet restore "Dragon.Business.csproj"

# Copy toàn bộ source code
COPY . .

# Build Native AOT
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
