FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["LabcomSync.csproj", "."]
RUN dotnet restore "LabcomSync.csproj"
COPY . .
RUN dotnet publish "LabcomSync.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
RUN groupadd -r appuser && useradd -r -g appuser appuser
RUN chown -R appuser:appuser /app
USER appuser
ENTRYPOINT ["dotnet", "LabcomSync.dll"]
