# build the dashboard
FROM node:20-alpine AS ui
WORKDIR /ui
COPY dashboard/package*.json ./
RUN npm ci
COPY dashboard/ ./
RUN npx vite build --outDir dist --emptyOutDir

# publish the collector
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Seerlens.Collector -c Release -o /app
COPY --from=ui /ui/dist /app/ui

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app ./
ENV SEERLENS_URL=http://0.0.0.0:5005
ENV SEERLENS_DB=/data/seerlens.db
EXPOSE 5005
ENTRYPOINT ["dotnet", "Seerlens.Collector.dll"]
