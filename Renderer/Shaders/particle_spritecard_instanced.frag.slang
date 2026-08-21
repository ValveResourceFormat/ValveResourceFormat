#version 460

// The instanced sprite card draws through the same pixel shader as every other card; only how its
// vertices arrive differs. Included rather than copied so the two cannot drift apart.
#include "particle_spritecard.frag.slang"
