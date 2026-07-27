# The build context needs the Hazel submodule, so clone with --recurse-submodules.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Hazel/Hazel/Hazel.csproj Hazel/Hazel/
COPY TimeMachine/TimeMachine.csproj TimeMachine/
RUN dotnet restore TimeMachine/TimeMachine.csproj

COPY Hazel/Hazel/ Hazel/Hazel/
COPY TimeMachine/ TimeMachine/
RUN dotnet publish TimeMachine/TimeMachine.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app

COPY --from=build /app .

EXPOSE 22023/udp

USER $APP_UID

ENTRYPOINT ["dotnet", "TimeMachine.dll"]
