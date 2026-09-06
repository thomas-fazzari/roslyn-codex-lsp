SOLUTION := RoslynCodexLsp.slnx

-include .env

EXTRA_PATH ?=
ifneq ($(strip $(EXTRA_PATH)),)
export PATH := $(EXTRA_PATH):$(PATH)
endif

CONFIGURATION ?= Debug
TEST_ARGS ?=

DOTNET ?= dotnet
BUN ?= bun
