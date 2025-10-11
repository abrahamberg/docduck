#!/bin/bash
# Test script for DocDuck API endpoints

set -e

API_URL="${API_URL:-http://localhost:5000}"

# Colors
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

echo -e "${YELLOW}Testing DocDuck API at $API_URL${NC}\n"

# Test 1: Health check
echo -e "${YELLOW}1. Testing /health endpoint...${NC}"
health_response=$(curl -s -w "\n%{http_code}" "$API_URL/health")
http_code=$(echo "$health_response" | tail -n1)
body=$(echo "$health_response" | sed '$d')

if [ "$http_code" = "200" ]; then
    echo -e "${GREEN}✓ Health check passed${NC}"
    echo "$body" | jq '.'
else
    echo -e "${RED}✗ Health check failed (HTTP $http_code)${NC}"
    echo "$body"
fi
echo ""

# Test 2: Root endpoint
echo -e "${YELLOW}2. Testing / endpoint...${NC}"
root_response=$(curl -s "$API_URL/")
echo "$root_response" | jq '.'
echo ""

# Test 3: Query endpoint
echo -e "${YELLOW}3. Testing /query endpoint...${NC}"
query_payload='{
  "question": "What is the main purpose of this project?",
  "topK": 5
}'

query_response=$(curl -s -X POST "$API_URL/query" \
  -H "Content-Type: application/json" \
  -d "$query_payload")

echo "$query_response" | jq '.'
echo ""

# Test 4: Chat endpoint
echo -e "${YELLOW}4. Testing /chat endpoint...${NC}"
chat_payload='{
  "message": "Tell me about the requirements",
  "history": [],
  "topK": 5
}'

chat_response=$(curl -s -X POST "$API_URL/chat" \
  -H "Content-Type: application/json" \
  -d "$chat_payload")

echo "$chat_response" | jq '.'
echo ""

# Test 5: Chat with history
echo -e "${YELLOW}5. Testing /chat with conversation history...${NC}"
chat_with_history='{
  "message": "Can you elaborate on that?",
  "history": [
    {
      "role": "user",
      "content": "What are the requirements?"
    },
    {
      "role": "assistant",
      "content": "The requirements include vector search and RAG capabilities."
    }
  ],
  "topK": 5
}'

chat_history_response=$(curl -s -X POST "$API_URL/chat" \
  -H "Content-Type: application/json" \
  -d "$chat_with_history")

echo "$chat_history_response" | jq '.'
echo ""

# Test 6: Error handling - empty question
echo -e "${YELLOW}6. Testing error handling (empty question)...${NC}"
error_payload='{"question": ""}'

error_response=$(curl -s -w "\n%{http_code}" -X POST "$API_URL/query" \
  -H "Content-Type: application/json" \
  -d "$error_payload")

error_code=$(echo "$error_response" | tail -n1)
error_body=$(echo "$error_response" | sed '$d')

if [ "$error_code" = "400" ]; then
    echo -e "${GREEN}✓ Error handling works correctly (HTTP 400)${NC}"
    echo "$error_body" | jq '.'
else
    echo -e "${RED}✗ Unexpected response (HTTP $error_code)${NC}"
    echo "$error_body"
fi
echo ""

echo -e "${GREEN}All tests completed!${NC}"
