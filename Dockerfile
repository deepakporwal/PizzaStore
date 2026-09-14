FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["PizzaStore.csproj", "./"]
RUN dotnet restore "PizzaStore.csproj"

COPY . .
RUN dotnet publish "PizzaStore.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

COPY --from=build /app/publish .
RUN mkdir -p /app/data

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "PizzaStore.dll"]
