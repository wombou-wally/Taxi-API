FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY  Taxi.Api/Taxi.Api.csproj Taxi.Api/
RUN dotnet restore  Taxi.Api
COPY  . .
RUN dotnet publish -c Release -o /app/publish --no-restore  Taxi.Api

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Taxi.Api.dll"]