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
COPY ["src/AndreGoepel.AppFoundation/AndreGoepel.AppFoundation.csproj", "src/AndreGoepel.AppFoundation/"]
COPY ["src/AndreGoepel.AppFoundation.Hosting/AndreGoepel.AppFoundation.Hosting.csproj", "src/AndreGoepel.AppFoundation.Hosting/"]
COPY ["src/AndreGoepel.AppFoundation.MailService/AndreGoepel.AppFoundation.MailService.csproj", "src/AndreGoepel.AppFoundation.MailService/"]
COPY ["src/AndreGoepel.AppFoundation.ServiceDefaults/AndreGoepel.AppFoundation.ServiceDefaults.csproj", "src/AndreGoepel.AppFoundation.ServiceDefaults/"]
COPY ["src/AndreGoepel.Website/AndreGoepel.Website.csproj", "src/AndreGoepel.Website/"]

RUN dotnet restore "src/AndreGoepel.Website/AndreGoepel.Website.csproj"

COPY src/ src/
WORKDIR "/src/src/AndreGoepel.Website"

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
# Do NOT use --no-restore here: Microsoft.AspNetCore.App.Internal.Assets is an
# implicit SDK package (not listed in .csproj) that provides _framework/blazor.web.js
# via the static-web-assets manifest. The csproj-only restore layer above won't
# fetch it; publish must be allowed to restore so the Blazor SDK task can include it.
RUN dotnet publish "AndreGoepel.Website.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "AndreGoepel.Website.dll"]
