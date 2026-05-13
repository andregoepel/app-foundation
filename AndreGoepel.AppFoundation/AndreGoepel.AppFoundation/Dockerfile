FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["AndreGoepel.AppFoundation/AndreGoepel.AppFoundation.csproj", "AndreGoepel.AppFoundation/"]
COPY ["AndreGoepel.Marten.Identity/AndreGoepel.Marten.Identity.csproj", "AndreGoepel.Marten.Identity/"]
COPY ["AndreGoepel.AppFoundation.MailService/AndreGoepel.AppFoundation.MailService.csproj", "AndreGoepel.AppFoundation.MailService/"]
COPY ["AndreGoepel.AppFoundation.ServiceDefaults/AndreGoepel.AppFoundation.ServiceDefaults.csproj", "AndreGoepel.AppFoundation.ServiceDefaults/"]
RUN dotnet restore "AndreGoepel.AppFoundation/AndreGoepel.AppFoundation.csproj"
COPY . .
WORKDIR "/src/AndreGoepel.AppFoundation"
RUN dotnet build "AndreGoepel.AppFoundation.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "AndreGoepel.AppFoundation.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "AndreGoepel.AppFoundation.dll"]
