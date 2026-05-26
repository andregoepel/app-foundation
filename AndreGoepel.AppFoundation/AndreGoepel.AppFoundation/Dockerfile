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
COPY ["AndreGoepel.AppFoundation/AndreGoepel.Marten.Identity.Abstractions/AndreGoepel.Marten.Identity.Abstractions.csproj", "AndreGoepel.AppFoundation/AndreGoepel.Marten.Identity.Abstractions/"]
COPY ["AndreGoepel.AppFoundation/AndreGoepel.Marten.Identity/AndreGoepel.Marten.Identity.csproj", "AndreGoepel.AppFoundation/AndreGoepel.Marten.Identity/"]
COPY ["AndreGoepel.AppFoundation/AndreGoepel.Marten.Identity.Blazor/AndreGoepel.Marten.Identity.Blazor.csproj", "AndreGoepel.AppFoundation/AndreGoepel.Marten.Identity.Blazor/"]
COPY ["AndreGoepel.AppFoundation/AndreGoepel.Website/AndreGoepel.Website.csproj", "AndreGoepel.AppFoundation/AndreGoepel.Website/"]

RUN dotnet restore "AndreGoepel.AppFoundation/AndreGoepel.AppFoundation/AndreGoepel.AppFoundation.csproj"

COPY AndreGoepel.AppFoundation/ AndreGoepel.AppFoundation/
WORKDIR "/src/AndreGoepel.AppFoundation/AndreGoepel.AppFoundation"
RUN dotnet build "AndreGoepel.AppFoundation.csproj" -c $BUILD_CONFIGURATION -o /app/build --no-restore

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "AndreGoepel.AppFoundation.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --no-restore

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "AndreGoepel.AppFoundation.dll"]
