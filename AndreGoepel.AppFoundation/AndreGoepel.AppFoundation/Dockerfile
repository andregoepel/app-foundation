FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["AndreGoepel.MembersArea/AndreGoepel.MembersArea.csproj", "AndreGoepel.MembersArea/"]
COPY ["AndreGoepel.Marten.Identity/AndreGoepel.Marten.Identity.csproj", "AndreGoepel.Marten.Identity/"]
COPY ["AndreGoepel.MembersArea.MailService/AndreGoepel.MembersArea.MailService.csproj", "AndreGoepel.MembersArea.MailService/"]
COPY ["AndreGoepel.MembersArea.ServiceDefaults/AndreGoepel.MembersArea.ServiceDefaults.csproj", "AndreGoepel.MembersArea.ServiceDefaults/"]
RUN dotnet restore "AndreGoepel.MembersArea/AndreGoepel.MembersArea.csproj"
COPY . .
WORKDIR "/src/AndreGoepel.MembersArea"
RUN dotnet build "AndreGoepel.MembersArea.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "AndreGoepel.MembersArea.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "AndreGoepel.MembersArea.dll"]
