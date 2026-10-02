FROM node:20-bookworm-slim

WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --omit=dev
COPY index.mjs ./
RUN mkdir -p /data/sessions

ENV NODE_ENV=production
EXPOSE 5080
CMD ["npm", "start"]
