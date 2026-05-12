# ─────────────────────────────────────────────────────────────────────────────
# Stage 1: BUILD — Compile .NET Native AOT app
# Dùng full SDK image (có shell, có apt) để build
# ─────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release

# Cài đặt clang (bắt buộc cho Native AOT cross-compile trên Linux)
RUN apt-get update && apt-get install -y --no-install-recommends \
    clang zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /src

# LAYER CACHING TRICK: Copy .csproj TRƯỚC để Docker cache lại bước restore
# → Nếu bạn chỉ sửa code C#, bước restore sẽ được lấy từ cache, không tải lại NuGet
COPY ["Dragon.Business.csproj", "./"]
COPY ["External/RedisFlow/src/RedisFlow/RedisFlow.csproj", "External/RedisFlow/src/RedisFlow/"]
RUN dotnet restore "Dragon.Business.csproj"

# Copy toàn bộ source code và build
COPY . .
RUN dotnet publish "Dragon.Business.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    /p:PublishAot=true

# Tạo thư mục Data (cần shell — chỉ có ở stage này)
RUN mkdir -p /app/publish/Data

# ─────────────────────────────────────────────────────────────────────────────
# Stage 2: HEALTHCHECK TOOL — Lấy wget từ Ubuntu Noble
#
# TẠI SAO cần stage này?
# noble-chiseled là "distroless" image: KHÔNG có shell, KHÔNG có curl/wget.
# Docker HEALTHCHECK cần một binary để chạy. Giải pháp: mượn wget từ Ubuntu
# Noble (cùng base với chiseled) → shared libraries tương thích 100%.
# ─────────────────────────────────────────────────────────────────────────────
FROM ubuntu:noble AS healthcheck-tools
RUN apt-get update && apt-get install -y --no-install-recommends wget \
    && rm -rf /var/lib/apt/lists/*

# ─────────────────────────────────────────────────────────────────────────────
# Stage 3: FINAL — Image production (siêu nhẹ + an toàn)
# noble-chiseled: non-root mặc định, không có shell, attack surface tối thiểu
# ─────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/runtime-deps:9.0-noble-chiseled AS final
WORKDIR /app

# Copy app binary từ build stage
COPY --from=build --chown=app:app /app/publish .

# Copy wget binary từ healthcheck-tools stage
# Chỉ copy binary, không copy toàn bộ Ubuntu — image vẫn siêu nhẹ!
COPY --from=healthcheck-tools /usr/bin/wget /usr/bin/wget

# Khai báo port (documentation + docker inspect)
EXPOSE 8080

# ── HEALTHCHECK ──────────────────────────────────────────────────────────────
# Mục đích: Docker engine biết container có "healthy" hay không
# Quan trọng với: docker run, docker-compose, local development
# Trong K8s: livenessProbe/readinessProbe trong manifest YAML thay thế cái này
#
# --interval=30s     : Kiểm tra mỗi 30 giây
# --timeout=5s       : Nếu wget không trả lời trong 5s → coi là fail
# --start-period=20s : Chờ 20s sau khi container start trước khi bắt đầu check
#                      (Native AOT start nhanh, nhưng cần warm-up DB connection)
# --retries=3        : Fail 3 lần liên tiếp → container bị đánh dấu "unhealthy"
# --spider           : wget chỉ kiểm tra HTTP status, không download body → nhanh
# ─────────────────────────────────────────────────────────────────────────────
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD ["/usr/bin/wget", "--no-verbose", "--tries=1", "--spider", \
         "http://localhost:8080/health/live"]

ENTRYPOINT ["./Dragon.Business"]
