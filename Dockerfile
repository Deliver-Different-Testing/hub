FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-env

RUN curl -fsSL https://deb.nodesource.com/setup_lts.x | bash - \
    && apt-get install -y nodejs

WORKDIR /App

COPY Hub.csproj Directory.Build.props Directory.Packages.props NuGet.Config global.json ./
RUN dotnet restore Hub.csproj

COPY . ./
RUN dotnet build Hub.csproj -c Release --property:OutputPath=/app
RUN dotnet publish Hub.csproj -c Release --property:PublishDir=/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 as base
COPY --from=build-env /publish /app
WORKDIR /app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Hub.dll"]
