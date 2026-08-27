FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Coophead.Relay.csproj .
RUN dotnet restore Coophead.Relay.csproj
COPY Program.cs .
RUN dotnet publish Coophead.Relay.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 27183
ENTRYPOINT ["dotnet", "Coophead.Relay.dll", "--port=27183"]
