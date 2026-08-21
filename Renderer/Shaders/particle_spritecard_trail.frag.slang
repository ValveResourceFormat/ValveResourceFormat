#version 460

// Trails draw through the same pixel shader as every other card; only how their vertices arrive
// differs. Included rather than copied so the two cannot drift apart.
#include "particle_spritecard.frag.slang"
