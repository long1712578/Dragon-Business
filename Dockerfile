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

# Tạo thư mục Data ngay tại giai đoạn Build (có shell)
RUN mkdir -p /app/publish/Data

# Giai đoạn Final (siêu nhẹ, không có shell)
FROM mcr.microsoft.com/dotnet/nightly/runtime-deps:9.0-noble-chiseled AS final
WORKDIR /app
# --chown đảm bảo file/thư mục thuộc user app (1654) từ đầu
COPY --from=build --chown=app:app /app/publish .
ENTRYPOINT ["./Dragon.Business"]
