# Use the official ASP.NET Core runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 7001

# Configure the application to listen on port 7001
ENV ASPNETCORE_URLS=http://+:7001

# Use the SDK image for building the application
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["AiPlusCourse.Api.csproj", "./"]
RUN dotnet restore "AiPlusCourse.Api.csproj"
COPY . .
WORKDIR /src
RUN dotnet build "AiPlusCourse.Api.csproj" -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish "AiPlusCourse.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Final stage/image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "AiPlusCourse.Api.dll"]
