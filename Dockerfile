FROM mcr.microsoft.com/dotnet/aspnet:5.0-focal as base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:5.0-focal AS build
WORKDIR /src
COPY ["src/", "."]
RUN dotnet build "HelloWorldService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "HelloWorldService.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "HelloWorldService.dll"]