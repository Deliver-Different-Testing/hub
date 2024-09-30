FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-env
WORKDIR /App

COPY *.csproj ./
RUN dotnet restore

COPY . ./
RUN dotnet build -c Release --property:OutputPath=/app
RUN dotnet publish -c Release --property:PublishDir=/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 as base
COPY --from=build-env /publish /app
WORKDIR /app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Hub.dll"]
