FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["WeddingGallery.Api/WeddingGallery.Api.csproj", "WeddingGallery.Api/"]
COPY ["WeddingGallery.Application/WeddingGallery.Application.csproj", "WeddingGallery.Application/"]
COPY ["WeddingGallery.Domain/WeddingGallery.Domain.csproj", "WeddingGallery.Domain/"]
COPY ["WeddingGallery.Infrastructure/WeddingGallery.Infrastructure.csproj", "WeddingGallery.Infrastructure/"]
RUN dotnet restore "WeddingGallery.Api/WeddingGallery.Api.csproj"

COPY . .
WORKDIR "/src/WeddingGallery.Api"
RUN dotnet build "WeddingGallery.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "WeddingGallery.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final

# FfmpegThumbnailGenerator shells out to ffmpeg to pull a poster frame out of uploaded
# videos. --no-install-recommends keeps this to the codecs we need rather than pulling in
# the whole multimedia stack.
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
EXPOSE 8080
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "WeddingGallery.Api.dll"]
