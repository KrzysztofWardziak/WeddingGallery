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
WORKDIR /app
EXPOSE 8080
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "WeddingGallery.Api.dll"]
