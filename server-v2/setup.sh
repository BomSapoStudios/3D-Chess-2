#!/bin/bash
# ─────────────────────────────────────────────────────────────────────────────
# 3D Chess Server v2 — One-time setup script
# Run this once from the server-v2/ directory on your Mac.
# ─────────────────────────────────────────────────────────────────────────────

set -e

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "  3D Chess Server v2 — Setup"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

# Check Node version
node_version=$(node --version 2>/dev/null || echo "not found")
echo ""
echo "Node.js: $node_version"
if [[ "$node_version" == "not found" || "${node_version:1:2}" -lt 20 ]]; then
  echo ""
  echo "⚠️  Node.js 20+ is required."
  echo "   Install via: brew install node  (or https://nodejs.org)"
  exit 1
fi

# Install dependencies
echo ""
echo "Installing dependencies..."
npm install

# Set up .env
if [ ! -f .env ]; then
  cp .env.example .env
  echo "Created .env from .env.example (using SQLite dev database)"
fi

# Generate Prisma client + push schema to SQLite
echo ""
echo "Setting up database (SQLite, zero config)..."
npm run db:generate
npm run db:push

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "  ✅ Setup complete!"
echo ""
echo "  Start the dev server:  npm run dev"
echo "  Run tests:             npm test"
echo "  Server runs on:        http://localhost:3001"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
