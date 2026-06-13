# Build context is the repository root so that Directory.{Build,Packages}.props
# (centralised package management) are visible to `dotnet restore`.

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Shared props live at the repository root.
COPY ["Directory.Build.props", "./"]
COPY ["Directory.Packages.props", "./"]

# Every csproj transitively referenced by the host. Test projects are
# intentionally excluded from the image layer.
COPY ["AndreGoepel.AppFoundation/AndreGoepel.AppFoundation/AndreGoepel.AppFoundation.csproj", "AndreGoepel.AppFoundation/AndreGoepel.AppFoundation/"]
COPY ["AndreGoepel.AppFoundation/AndreGoepel.AppFoundation.MailService/AndreGoepel.AppFoundation.MailService.csproj", "AndreGoepel.AppFoundation/AndreGoepel.AppFoundation.MailService/"]
COPY ["AndreGoepel.AppFoundation/AndreGoepel.AppFoundation.ServiceDefaults/AndreGoepel.AppFoundation.ServiceDefaults.csproj", "AndreGoepel.AppFoundation/AndreGoepel.AppFoundation.ServiceDefaults/"]
COPY ["AndreGoepel.AppFoundation/AndreGoepel.Website/AndreGoepel.Website.csproj", "AndreGoepel.AppFoundation/AndreGoepel.Website/"]

RUN dotnet restore "AndreGoepel.AppFoundation/AndreGoepel.AppFoundation/AndreGoepel.AppFoundation.csproj"

COPY AndreGoepel.AppFoundation/ AndreGoepel.AppFoundation/
WORKDIR "/src/AndreGoepel.AppFoundation/AndreGoepel.AppFoundation"

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
# Do NOT use --no-restore here: Microsoft.AspNetCore.App.Internal.Assets is an
# implicit SDK package (not listed in .csproj) that provides _framework/blazor.web.js
# via the static-web-assets manifest. The csproj-only restore layer above won't
# fetch it; publish must be allowed to restore so the Blazor SDK task can include it.
RUN dotnet publish "AndreGoepel.AppFoundation.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "AndreGoepel.AppFoundation.dll"]
