#!/bin/bash
# Build script for Angular ARAN Dashboard deployment to IIS sub-path
# Usage: ./build-for-iis.sh /pbi_srv/zivarantest/

set -e

# Color codes
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Configuration
PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUTPUT_DIR="$PROJECT_DIR/dist/ziv-aran-dashboard"
DEFAULT_BASE_HREF="/pbi_srv/zivarantest/"

# Get base-href from argument or use default
BASE_HREF="${1:-$DEFAULT_BASE_HREF}"

# Ensure trailing slash
if [[ ! "$BASE_HREF" =~ /$ ]]; then
    BASE_HREF="${BASE_HREF}/"
fi

echo -e "${GREEN}================================${NC}"
echo -e "${GREEN}Angular ARAN Dashboard Builder${NC}"
echo -e "${GREEN}================================${NC}"
echo ""

echo "Project Directory: $PROJECT_DIR"
echo "Base Href: $BASE_HREF"
echo "Output Directory: $OUTPUT_DIR"
echo ""

# Check if node_modules exists
if [ ! -d "$PROJECT_DIR/node_modules" ]; then
    echo -e "${YELLOW}Installing dependencies...${NC}"
    npm install
    echo ""
fi

# Clean previous builds
echo -e "${YELLOW}Cleaning previous build...${NC}"
rm -rf "$OUTPUT_DIR"
echo -e "${GREEN}✓ Clean complete${NC}"
echo ""

# Build production bundle
echo -e "${YELLOW}Building production bundle...${NC}"
npm run build -- --configuration=production --base-href="$BASE_HREF"

if [ $? -eq 0 ]; then
    echo -e "${GREEN}✓ Build successful${NC}"
    echo ""
else
    echo -e "${RED}✗ Build failed${NC}"
    exit 1
fi

# Display build results
echo -e "${YELLOW}Build Statistics:${NC}"
echo "================================"

if [ -d "$OUTPUT_DIR" ]; then
    TOTAL_SIZE=$(du -sh "$OUTPUT_DIR" | cut -f1)
    FILE_COUNT=$(find "$OUTPUT_DIR" -type f | wc -l)
    
    echo "Total Size: $TOTAL_SIZE"
    echo "Files: $FILE_COUNT"
    echo ""
    
    echo -e "${YELLOW}Key Files:${NC}"
    
    if [ -f "$OUTPUT_DIR/index.html" ]; then
        INDEX_SIZE=$(du -h "$OUTPUT_DIR/index.html" | cut -f1)
        echo "  index.html: $INDEX_SIZE"
        
        # Check for base href in index.html
        if grep -q "base href=\"$BASE_HREF\"" "$OUTPUT_DIR/index.html"; then
            echo -e "  ${GREEN}✓ Base href correctly set to: $BASE_HREF${NC}"
        else
            echo -e "  ${RED}✗ Base href NOT found in index.html${NC}"
        fi
    fi
    
    if [ -f "$OUTPUT_DIR/styles.css" ] || [ -f "$OUTPUT_DIR/styles.*.css" ]; then
        STYLES=$(ls -h "$OUTPUT_DIR"/styles*.css 2>/dev/null | head -1 | xargs du -h | cut -f1)
        echo "  Styles: $STYLES"
    fi
    
    MAIN_BUNDLES=$(ls -h "$OUTPUT_DIR"/main*.js 2>/dev/null | head -1 | xargs du -h | cut -f1)
    echo "  Main Bundle: $MAIN_BUNDLES"
    
    echo ""
    echo -e "${GREEN}Build artifacts location:${NC}"
    echo "  $OUTPUT_DIR"
    echo ""
    
    echo -e "${YELLOW}Next Steps for IIS Deployment:${NC}"
    echo "  1. Copy all files from above directory to IIS physical path"
    echo "  2. Configure web.config with URL rewrite rules"
    echo "  3. Update IIS Application base path to: $BASE_HREF"
    echo "  4. Test in browser: http://your-server${BASE_HREF}"
    echo ""
    
else
    echo -e "${RED}✗ Output directory not found${NC}"
    exit 1
fi

echo -e "${GREEN}✓ Build complete!${NC}"
