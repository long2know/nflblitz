const identity = new Float32Array([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]);

function requireValue(condition, message) {
  if (!condition) throw new Error(message);
}

const venuePrefixes = 'CAR FAL RAV BIL PAN BEA BEN BRW COW BRO LIO PAC COL JAG CHI DOL VIK PAT SAI GIA JET RAI EAG STE CHA FOR SEA RAM BUC TIT RED'.split(' ');
const venueFieldTables = ['field2_textures', 'field2_textures', 'astro_turf_field_textures',
  'dirt_field_textures', 'snow_field_textures', 'asphalt_field_textures', 'mud_field_textures'];
const venueFieldNames = 'Beabg Beaez1 Beaez2 Beaez3 ads98 blitz98 grass198 grass198 grass298 grass398 grass398 nfl98 nflpa98 nums98 post98 roof98'.split(' ');
const venueStadiumNames = 'ads98 blitz98 ceil98 crowdd98 crowdl98 post98 roof98 skybox98 trim98 trim98 wall98'.split(' ');

function requireVenue(venue) {
  const selected = venue?.selectionSchema === 'blitz-source-environment/v2';
  requireValue(venue?.stableId === 'source-field2-stadpie' && /^[0-9a-f]{64}$/.test(venue.manifest) &&
    Number.isInteger(venue.field) && venue.field >= 0 && venue.field <= 6 &&
    Number.isInteger(venue.stadium) && venue.stadium >= 0 && venue.stadium <= 3 &&
    venueFieldTables[venue.field] === venue.fieldTextureTable &&
    venue.stadiumTextureTable === (venue.stadium === 0 ? 'day_stadium_textures' :
      venue.stadium === 2 ? 'snow_stadium_textures' : 'stadium_textures') &&
    (selected ? Number.isInteger(venue.homeTeamIndex) && venue.homeTeamIndex >= 0 && venuePrefixes[venue.homeTeamIndex] === venue.homePrefix &&
      venue.selectionId === `source-field2-stadpie/field-${venue.field}/stadium-${venue.stadium}/${venue.homePrefix}` &&
      venue.alternateVenuesAdmitted === true && venue.teamEndZonesBound === true :
      [undefined, 'blitz-source-environment/v1'].includes(venue.selectionSchema) &&
      venue.selectionId == null && venue.field === 0 && venue.stadium === 0 && venue.homePrefix === 'BEA' &&
      venue.alternateVenuesAdmitted === false && venue.teamEndZonesBound === false),
  'SOURCE_WORLD_VENUE: explicit source selector/table/team identity or unchanged historic grass/day/BEA required.');
  return selected;
}

function venueMaterial(venue, slot) {
  requireValue(Number.isInteger(slot) && slot >= 0 && slot < 27,
    'SOURCE_WORLD_VENUE: invalid original material slot.');
  let name = slot < 16 ? venueFieldNames[slot] : venueStadiumNames[slot - 16];
  if (slot >= 16) {
    if (venue.stadium === 0 || venue.stadium === 2) name = name.replace(/98$/, venue.stadium === 0 ? '_d' : '_s');
  } else if (slot < 4) name = venue.homePrefix + name.slice(3);
  else if (venue.field >= 2) {
    const suffix = ['', '', 't', 'd', 's', 'a', 'm'][venue.field];
    if (slot >= 6 && slot <= 10) name = `${slot < 8 ? 'grass1' : slot === 8 ? 'grass2' : 'grass3'}_${suffix}`;
    else if (slot === 13 || [4, 5, 6].includes(venue.field) && [5, 11, 12].includes(slot) ||
      venue.field === 4 && [4, 14, 15].includes(slot)) name = name.replace(/98$/, '_' + suffix);
  }
  return name + '.wms';
}

function requireVenueBinding(target, draw) {
  const state = draw.state, venue = state.venue;
  if (!requireVenue(venue)) return;
  const slot = state.materialSlot, expected = venueMaterial(venue, slot), texture = target.textures.get(draw.texture);
  const authored = (slot < 16 ? venueFieldNames[slot] : venueStadiumNames[slot - 16]) + '.wms';
  requireValue(state.sourceSet === (slot < 16 ? 'field2' : 'stadpie') &&
    !(venue.stadium === 3 && slot >= 16) &&
    state.authoredName?.toLowerCase() === authored.toLowerCase() &&
    state.selectedName?.toLowerCase() === expected.toLowerCase() &&
    [expected, expected.replace(/\.wms$/, '.tga')].some(name =>
      state.sourcePath?.toLowerCase() === 'img3d/' + name.toLowerCase()) &&
    /^[0-9a-f]{64}$/.test(state.textureSha256) && texture?.sha256 === state.textureSha256 &&
    texture.origin?.startsWith(state.sourcePath + ' / '),
  `SOURCE_WORLD_VENUE: missing/mismatched selected original material ${expected}; no grass/day/Bears substitution.`);
}

function program(gl, vertex, fragment) {
  const compile = (type, source) => {
    const shader = gl.createShader(type);
    gl.shaderSource(shader, source);
    gl.compileShader(shader);
    if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(shader));
    return shader;
  };
  const result = gl.createProgram();
  const vert = compile(gl.VERTEX_SHADER, vertex), frag = compile(gl.FRAGMENT_SHADER, fragment);
  gl.attachShader(result, vert); gl.attachShader(result, frag); gl.linkProgram(result);
  gl.deleteShader(vert); gl.deleteShader(frag);
  if (!gl.getProgramParameter(result, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(result));
  return result;
}

export function sourceObservation(status, audioReceiver, ready, failed) {
  const { cameraTrace, ...camera } = status?.camera ?? {};
  return structuredClone({
    ready, failed, state: status ? { ...status, camera } : status, audioReceiver,
  });
}

const sceneVertex = `#version 300 es
layout(location=0) in vec3 position;
layout(location=1) in vec2 uv;
uniform mat4 viewProjection;
uniform mat4 model;
uniform vec4 cameraDepthRow;
out vec2 texcoord;
out float cameraDepth;
void main() {
  texcoord=uv;
  vec4 world=model*vec4(position,1.0);
  cameraDepth=dot(cameraDepthRow,world);
  gl_Position=viewProjection*world;
}`;
const sceneFragment = `#version 300 es
precision highp float;
in vec2 texcoord;
in float cameraDepth;
uniform sampler2D atlas;
uniform float cutoff;
uniform bool constantAlpha;
uniform float constantAlphaValue;
uniform bool textureTimesAlpha;
uniform float playerFlashAdd;
uniform vec4 identityOutput;
uniform bool fogEnabled;
uniform vec3 fogColor;
uniform vec2 fogRange;
out vec4 pixel;
void main() {
  vec4 color=texture(atlas,texcoord);
  if(constantAlpha) color.a=constantAlphaValue;
  if(color.a<=cutoff) discard;
  if(textureTimesAlpha) color.rgb*=color.a;
  color.rgb=clamp(color.rgb+playerFlashAdd,0.0,1.0);
  if(fogEnabled) color.rgb=mix(color.rgb,fogColor,clamp((cameraDepth-fogRange.x)/(fogRange.y-fogRange.x),0.0,1.0));
  pixel=identityOutput.a>0.0?identityOutput:color;
}`;
const hudVertex = `#version 300 es
layout(location=0) in vec2 position;
layout(location=1) in vec2 uv;
layout(location=2) in vec4 color;
uniform vec2 contentSize;
out vec2 texcoord;
out vec4 vertexColor;
void main() {
  texcoord=uv; vertexColor=color;
  gl_Position=vec4(position.x/contentSize.x*2.0-1.0,1.0-position.y/contentSize.y*2.0,0.0,1.0);
}`;
const hudFragment = `#version 300 es
precision highp float;
in vec2 texcoord;
in vec4 vertexColor;
uniform sampler2D atlas;
uniform float highlightAdd;
out vec4 pixel;
void main() {
  vec4 color=texture(atlas,texcoord)*vertexColor;
  pixel=vec4(clamp(color.rgb+highlightAdd,0.0,1.0),color.a);
}`;
export const fireVertex = `#version 300 es
layout(location=0) in vec3 position;
layout(location=1) in vec2 uv;
uniform mat4 sourceProjection;
out vec2 texcoord;
out float cameraDepth;
void main() { texcoord=uv; cameraDepth=position.z; gl_Position=sourceProjection*vec4(position,1.0); }`;
export const fireFragment = `#version 300 es
precision highp float;
in vec2 texcoord;
in float cameraDepth;
uniform sampler2D atlas;
uniform bool proofMask;
uniform bool fogEnabled;
uniform vec3 fogColor;
uniform vec2 fogRange;
out vec4 pixel;
void main() {
  vec4 texel=texture(atlas,texcoord);
  vec4 color=vec4(texel.rgb*texel.a,texel.a);
  if(fogEnabled) color.rgb=mix(color.rgb,fogColor,clamp((cameraDepth-fogRange.x)/(fogRange.y-fogRange.x),0.0,1.0));
  pixel=proofMask?vec4(texel.a):color;
}`;

export const shadowVertex = `#version 300 es
layout(location=0) in vec3 raster;
layout(location=1) in vec2 st;
uniform mat4 sourceProjection;
uniform vec2 rasterSize;
uniform float normalizedDepthBias;
out vec2 texcoord;
out float cameraDepth;
void main() {
  cameraDepth=1.0/raster.z;
  vec4 projected=sourceProjection*vec4(0.0,0.0,cameraDepth,1.0);
  gl_Position=vec4((2.0*raster.xy/rasterSize-1.0)*cameraDepth,
    projected.z-2.0*normalizedDepthBias*cameraDepth,cameraDepth);
  texcoord=vec2(st.x/256.0,1.0-st.y/256.0);
}`;
export const shadowFragment = `#version 300 es
precision highp float;
in vec2 texcoord;
in float cameraDepth;
uniform sampler2D atlas;
uniform float cutoff;
uniform bool identityPass;
uniform bool fogEnabled;
uniform vec3 fogColor;
uniform vec2 fogRange;
out vec4 pixel;
void main() {
  vec4 texel=texture(atlas,texcoord);
  if(texel.a<=cutoff) discard;
  vec4 color=vec4(texel.rgb*texel.a,texel.a);
  if(fogEnabled) color.rgb=mix(color.rgb,fogColor,clamp((cameraDepth-fogRange.x)/(fogRange.y-fogRange.x),0.0,1.0));
  pixel=identityPass?vec4(190.0/255.0,0.0,0.0,1.0):color;
}`;

export class SourceRenderer {
  constructor(canvas) {
    this.canvas = canvas;
    const gl = canvas.getContext('webgl2', {
      alpha: false, antialias: true, preserveDrawingBuffer: true, powerPreference: 'high-performance',
    });
    requireValue(gl, 'Actual WebGL2 is required. No image/Canvas2D/JavaScript-gameplay fallback exists.');
    this.gl = gl;
    this.textures = new Map(); this.meshes = new Map(); this.appearances = new Map();
    this.models = new Map(); this.world = []; this.worldSelections = new Map(); this.frameCount = 0; this.meshUploads = 0;
    this.scene = program(gl, sceneVertex, sceneFragment);
    this.hud = program(gl, hudVertex, hudFragment);
    this.fire = program(gl, fireVertex, fireFragment);
    this.sceneUniforms = Object.fromEntries(['viewProjection', 'model', 'atlas', 'cutoff', 'constantAlpha',
      'constantAlphaValue', 'textureTimesAlpha', 'identityOutput', 'playerFlashAdd',
      'cameraDepthRow', 'fogEnabled', 'fogColor', 'fogRange'].map(name => [name, gl.getUniformLocation(this.scene, name)]));
    this.hudUniforms = Object.fromEntries(['contentSize', 'atlas', 'highlightAdd'].map(name => [name, gl.getUniformLocation(this.hud, name)]));
    this.fireUniforms = Object.fromEntries(['sourceProjection', 'atlas', 'proofMask', 'fogEnabled', 'fogColor', 'fogRange']
      .map(name => [name, gl.getUniformLocation(this.fire, name)]));
    this.fireBuffer = gl.createBuffer(); this.fireVao = gl.createVertexArray();
    gl.bindVertexArray(this.fireVao); gl.bindBuffer(gl.ARRAY_BUFFER, this.fireBuffer);
    gl.enableVertexAttribArray(0); gl.vertexAttribPointer(0, 3, gl.FLOAT, false, 20, 0);
    gl.enableVertexAttribArray(1); gl.vertexAttribPointer(1, 2, gl.FLOAT, false, 20, 12);
    this.hudBuffer = gl.createBuffer();
    this.hudVao = gl.createVertexArray();
    gl.bindVertexArray(this.hudVao);
    gl.bindBuffer(gl.ARRAY_BUFFER, this.hudBuffer);
    for (const [location, size, offset] of [[0, 2, 0], [1, 2, 8], [2, 4, 16]]) {
      gl.enableVertexAttribArray(location); gl.vertexAttribPointer(location, size, gl.FLOAT, false, 32, offset);
    }
    gl.bindVertexArray(null);
    gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
    gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, false);
    gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, false);
    gl.pixelStorei(gl.UNPACK_COLORSPACE_CONVERSION_WEBGL, gl.NONE);
    const debug = gl.getExtension('WEBGL_debug_renderer_info');
    this.device = {
      version: gl.getParameter(gl.VERSION), shadingLanguage: gl.getParameter(gl.SHADING_LANGUAGE_VERSION),
      vendor: gl.getParameter(gl.VENDOR), renderer: gl.getParameter(gl.RENDERER),
      unmaskedVendor: debug ? gl.getParameter(debug.UNMASKED_VENDOR_WEBGL) : null,
      unmaskedRenderer: debug ? gl.getParameter(debug.UNMASKED_RENDERER_WEBGL) : null,
      contextAttributes: gl.getContextAttributes(),
    };
    this.assertHealthy('WebGL initialization');
  }

  assertHealthy(stage) {
    requireValue(!this.gl.isContextLost(), `Actual WebGL context was lost at ${stage}.`);
    const error = this.gl.getError();
    requireValue(error === this.gl.NO_ERROR, `WebGL error ${error} at ${stage}.`);
  }

  texture(id, width, height, source, mipCount, repeat, linear, sha256, origin) {
    const gl = this.gl;
    requireValue(!this.textures.has(id) && width > 0 && height > 0 && /^[0-9a-f]{64}$/.test(sha256),
      `Invalid/duplicate verified texture ${id}.`);
    const data = source instanceof Uint8Array ? source : Uint8Array.from(source);
    const texture = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, texture);
    let offset = 0;
    for (let mip = 0; mip < mipCount; mip++) {
      const w = Math.max(1, width >> mip), h = Math.max(1, height >> mip), size = w * h * 4;
      requireValue(offset + size <= data.length, `Original mip payload is truncated: ${id}.`);
      gl.texImage2D(gl.TEXTURE_2D, mip, gl.RGBA8, w, h, 0, gl.RGBA, gl.UNSIGNED_BYTE, data.subarray(offset, offset + size));
      offset += size;
    }
    requireValue(offset === data.length, `Original texture byte inventory changed: ${id}.`);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_BASE_LEVEL, 0);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAX_LEVEL, mipCount - 1);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, linear ?
      mipCount > 1 ? gl.LINEAR_MIPMAP_NEAREST : gl.LINEAR : gl.NEAREST);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, linear ? gl.LINEAR : gl.NEAREST);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, repeat ? gl.REPEAT : gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, repeat ? gl.REPEAT : gl.CLAMP_TO_EDGE);
    this.textures.set(id, { texture, width, height, mipCount, repeat, linear, sha256, origin });
    this.assertHealthy(`original texture ${id}`);
  }

  mesh(id, xyzuv, indices) {
    requireValue(!this.meshes.has(id) && xyzuv.length > 0 && xyzuv.length % 5 === 0 &&
      indices.length > 0 && indices.length % 3 === 0, `Invalid/duplicate original mesh ${id}.`);
    requireValue(indices.every(i => Number.isInteger(i) && i >= 0 && i < xyzuv.length / 5),
      `Original mesh has an invalid triangle index: ${id}.`);
    const gl = this.gl, vao = gl.createVertexArray();
    gl.bindVertexArray(vao);
    const vertex = gl.createBuffer(), index = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, vertex);
    gl.bufferData(gl.ARRAY_BUFFER, Float32Array.from(xyzuv), gl.STATIC_DRAW);
    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, index);
    gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, Uint32Array.from(indices), gl.STATIC_DRAW);
    gl.enableVertexAttribArray(0); gl.vertexAttribPointer(0, 3, gl.FLOAT, false, 20, 0);
    gl.enableVertexAttribArray(1); gl.vertexAttribPointer(1, 2, gl.FLOAT, false, 20, 12);
    gl.bindVertexArray(null);
    this.meshes.set(id, { vao, vertex, index, count: indices.length, vertices: xyzuv.length / 5 });
    this.meshUploads++;
    this.assertHealthy(`original mesh ${id}`);
  }

  worldDraw(mesh, texture, state) {
    requireValue(this.meshes.has(mesh) && this.textures.has(texture), `Unbound original world draw ${mesh}.`);
    const draw = { mesh, texture, state: JSON.parse(state) };
    if (draw.state.venue) requireVenueBinding(this, draw);
    if (draw.state.venue?.selectionSchema === 'blitz-source-environment/v2') {
      this.worldSelections ??= new Map();
      const key = `${draw.state.venue.selectionId}/${draw.state.venue.manifest}`;
      if (!this.worldSelections.has(key)) this.worldSelections.set(key, []);
      const group = this.worldSelections.get(key);
      requireValue(!group.some(previous => previous.mesh === mesh),
        'SOURCE_WORLD_VENUE: duplicate mesh in an authenticated selected draw group.');
      group.push(draw);
      if (this.world.length === 0) this.world = group;
    } else this.world.push(draw);
  }

  model(id, joints) {
    const value = JSON.parse(joints);
    requireValue(!this.models.has(id) && id >= 0 && id < 32 && value.length === 15 &&
      value.every(j => j.parts.length > 0 && j.parts.every(p => this.meshes.has(p.mesh))),
    `Missing actual fifteen-limb original model ${id}.`);
    this.models.set(id, value);
  }

  appearance(id, metadata) {
    const value = JSON.parse(metadata);
    requireValue(!this.appearances.has(id) && value.tint === 'none' && value.textures.length >= 7 &&
      value.textures.every(t => this.textures.has(t)) && this.meshes.has(value.numberMesh) &&
      value.textureHashes.every((hash, i) => hash === this.textures.get(value.textures[i]).sha256),
    `Invalid original uniform/number bindings ${id}; no tint or alternate-team atlas substitution.`);
    if (value.headVariant === 'original-phead-source-authored')
      requireValue(value.headJoint === 8 && value.headParts?.length === 1 && value.headParts[0].slot === 1 &&
        value.headParts[0].mesh === 'limbs/limb_phead_obj/1' &&
        value.headSourceSha256 === '3fb4e566b57670e397fde16c6f515d06c95379420074c2d6c7b111bca843e257' &&
        value.headGeometrySha256 === 'f835cda5498db7b3f2783b3cfda99d07a2ef765f821f1655c1b4be3d555a57b5' &&
        this.meshes.has(value.headParts[0].mesh),
      'Original popped-head binding requires actual SDL phead geometry/HEDSHU1 at joint8, not a helmet/custom-head substitute.');
    this.appearances.set(id, value);
  }

  freeBall(mesh, texture, alternateTexture, metadata) {
    const value = JSON.parse(metadata);
    requireValue(mesh === 'original/free-ball/limb_medball_obj_big' && this.meshes.get(mesh)?.count === 144 &&
      texture === 'teams/runtime/HEDSHU.WMS' && alternateTexture === 'teams/runtime/HEDSHU2.WMS' &&
      this.textures.get(texture)?.sha256 === value.textureSha256 &&
      this.textures.get(alternateTexture)?.sha256 === value.alternateTextureSha256 && !this.ball &&
      value.textureSha256 === 'b2a5795cc67286d6a9a1c60c21f92f393134ab81f18dedfad84ffe405edb3fa3' &&
      value.alternateTextureSha256 === 'b59af47bf7baf6a2bcffacd4fb68af206229099102c00baf608d771f4ac5836e' &&
      value.origin === 'original-medball2-SDL-source-authored-not-native-medball2.h' &&
      value.sourceSha256 === 'd54218dd7d0fc45fe7ad7072cb39b12913dfed80687999f69d5e93a269bc9704' &&
      value.geometrySha256 === '22b92aa3f0b113be4c18edef70a6503fac448c1d85ff187b84b68c23b8d2eada' &&
      value.sourcePivot === 'authoring-origin-unchanged' && value.sourceBasis === 'original-source-XYZ' &&
      value.sourceTriangleOrder === 'Renderer321' && value.rawTextureCode === 0 && value.sourceScale === 1 &&
      value.nativeGeneratedHeaderEquivalent === false,
    'Exact source medball2 geometry/authoring pivot/HEDSHU decals required; no held-ball extraction, recenter or sphere fallback.');
    this.ball = { mesh, texture, alternateTexture, origin: value.origin, metadata: value };
  }

  effects(projection, vertices, textures, rawFlags, flashAlpha, metadata) {
    const styles = JSON.parse(metadata), frame = styles.frame;
    if (styles.world) {
      SourceRenderer.prototype.worldPresentation.call(this, projection, styles.world);
      requireValue(this.worldFx.metadata.shadows.actors.every((actor, i) => actor.rawFlags === rawFlags[i]),
        'SOURCE_WORLD_ACTOR_FLAGS: world and existing effects must retain the same actual source actor flags.');
    } else requireValue(!this.worldFx, 'SOURCE_WORLD_PACKET_MISSING: an enabled world composition must supply every frame.');
    requireValue(projection.length === 16 && rawFlags.length === 14 && flashAlpha.length === 14 &&
      vertices.length === textures.length * 6 * 5, 'Incomplete authoritative C# effect/actor-flash packet.');
    requireValue(styles.hudConstantColor === 0xc0000000 && styles.hudCombiner === 'DECAL_TEXTURE_ADD_CCALPHA' &&
      Array.isArray(styles.hudHighlight) && styles.hudHighlight.every(i => Number.isInteger(i) && i >= 0) &&
      new Set(styles.hudHighlight).size === styles.hudHighlight.length,
    'Missing/invalid authoritative source meter primitive/material packet.');
    requireValue(frame.enabled || textures.length === 0, 'Disabled Core fire cannot submit renderer-side particles.');
    requireValue(!frame.enabled || textures.every(t => Number.isInteger(t) && t >= 0 && t < 27 && this.textures.has(`fire/${t}`)),
      'Actual Core particle selected an unbound original TRAIL/DIRT frame.');
    requireValue(flashAlpha.every((alpha, actor) => alpha === ((rawFlags[actor] & 0x400) ? 192 / 255 : 0)),
      'Only actual raw0x400 may request the original DECAL_TEXTURE_ADD_CCALPHA/0xc0000000 combiner.');
    const helmets = SourceRenderer.prototype.helmetPackets.call(this, styles);
    this.fx = {
      projection: Float32Array.from(projection), vertices: Float32Array.from(vertices),
      textures: Array.from(textures), rawFlags: Array.from(rawFlags), flashAlpha: Array.from(flashAlpha), metadata: frame,
      hudHighlight: new Set(styles.hudHighlight), hudStyle: styles,
      helmets, helmetAsset: styles.helmetAsset,
    };
  }

  worldPresentation(projection, packet) {
    const state = packet?.state, shadow = state?.shadows, fog = state?.fog;
    const integer = value => Number.isSafeInteger(value) && value >= 0;
    requireValue(state?.schema === 'blitz-source-world/v1' && state.enabled === true &&
      integer(state.epoch) && integer(state.presentSequence) && state.presentSequence > 0 &&
      integer(state.sourceTick) && (integer(state.cameraSourceTick) || state.cameraSourceTick === -1 && state.sourceTick === 0) &&
      state.cameraSourceTick <= state.sourceTick &&
      integer(state.sourceFramesObserved) && state.sourceFramesObserved > 0 &&
      state.hres === 512 && [256, 384].includes(state.vres) &&
      Number.isFinite(state.renderInterpolation01) && state.renderInterpolation01 >= 0 && state.renderInterpolation01 <= 1 &&
      state.sourceRevision === '89f840d58d152a2a9f230cb8d0cdffbd554c8ded' &&
      state.playerGitBlob === '5744c51f30b625b9f80b92388ab49ff0ebc5bb2c' &&
      state.fieldGitBlob === '2821ba68986cb7e62c42cf920747c5ba9b91ea37',
    'SOURCE_WORLD_SCHEMA: explicit v1 source frame, raster and pinned source identities required.');
    const previous = this.worldFx?.metadata;
    requireValue(!previous || state.presentSequence > previous.presentSequence &&
      (state.epoch === previous.epoch && state.sourceTick >= previous.sourceTick &&
        state.sourceFramesObserved === previous.sourceFramesObserved + Number(state.sourceTick !== previous.sourceTick) ||
       state.epoch > previous.epoch && state.sourceFramesObserved === 1),
    'SOURCE_WORLD_CLOCK: duplicate, reordered or stale world frame/epoch; renderer clocks never advance source state.');
    const selectedVenue = requireVenue(state.venue);
    const draws = selectedVenue && this.worldSelections?.size ?
      this.worldSelections.get(`${state.venue.selectionId}/${state.venue.manifest}`) : this.world;
    requireValue(projection?.length === 16 && Array.from(projection).every(Number.isFinite) &&
      state.cameraDepthRow?.length === 4 && state.cameraDepthRow.every(Number.isFinite) &&
      draws?.length > 0 && draws.every(draw => [0x10000, 0x48000].includes(draw.state.sourceDrawOrder) &&
        (selectedVenue ? draw.state.venue?.selectionId === state.venue.selectionId &&
          draw.state.venue.manifest === state.venue.manifest :
          draw.state.venue == null || !requireVenue(draw.state.venue) && draw.state.venue.manifest === state.venue.manifest)),
    'SOURCE_WORLD_VENUE: camera depth row and uploaded world must match the explicit authenticated selection.');
    requireValue(!previous || !selectedVenue || previous.venue?.manifest === state.venue.manifest &&
      previous.venue?.selectionId === state.venue.selectionId ||
      state.epoch > previous.epoch, 'SOURCE_WORLD_VENUE: a different home selection requires the actual new-match epoch.');
    if (selectedVenue) for (const draw of draws) requireVenueBinding(this, draw);
    requireValue(fog && typeof fog.configurationBound === 'boolean' && typeof fog.enabled === 'boolean' &&
      ['Unbound', 'Versus', 'UserConfiguration'].includes(fog.origin) &&
      fog.configurationBound === (fog.origin !== 'Unbound') &&
      integer(fog.colorRgb) && fog.colorRgb <= 0xffffff &&
      fog.onDrawOrder === 0x8000 && fog.offDrawOrder === 0x58000 &&
      (fog.origin === 'UserConfiguration' ?
        [null, true, false].includes(fog.sourceEnabled) && fog.enabled === (fog.sourceEnabled === true) &&
        fog.userConfiguration?.sourceKey === 4 && integer(fog.userConfiguration.selection) &&
        fog.userConfiguration.selection <= 4 && fog.userConfiguration.initializations === 1 &&
        [fog.isFoggy, fog.fogMask, fog.thickFogMask].every(value => value === null || integer(value)) &&
        (!fog.enabled || fog.isFoggy === 1) :
       fog.configurationBound ? [fog.isFoggy, fog.fogMask, fog.thickFogMask].every(integer) :
        fog.isFoggy === null && fog.fogMask === null && fog.thickFogMask === null && !fog.enabled) &&
      (fog.enabled ? fog.configurationBound && fog.start === 1 && Number.isFinite(fog.end) && fog.end > 1 :
        fog.start === 0 && fog.end === 0 && fog.colorRgb === 0),
    'SOURCE_WORLD_FOG: source-owned enable/disable, exact range/color and explicit bound/unbound operands required.');
    requireValue(shadow?.rasterStride === 5 && shadow.selectorStride === 4 &&
      shadow.sourceDrawOrder === 0x20200 && shadow.inheritedCull === 'GR_CULL_NEGATIVE' &&
      shadow.alphaSource === 'GR_ALPHASOURCE_TEXTURE_ALPHA' && shadow.alphaFunction === 'GR_CMP_GREATER' &&
      shadow.alphaValue === 100 && shadow.colorFunction === 'GR_COLORCOMBINE_TEXTURE_TIMES_ALPHA' &&
      shadow.textureFunction === 'GR_TEXTURECOMBINE_DECAL' && shadow.clampMode === 'GR_TEXTURECLAMP_WRAP' &&
      shadow.filter === 'BILINEAR' && shadow.depthMode === 'GR_DEPTHBUFFER_WBUFFER' &&
      shadow.depthFunction === 'GR_CMP_GREATER' && shadow.depthWrite === true &&
      shadow.rgbSource === 'GR_BLEND_SRC_ALPHA' && shadow.rgbDestination === 'GR_BLEND_ONE_MINUS_SRC_ALPHA' &&
      shadow.alphaSourceBlend === 'GR_BLEND_SRC_ALPHA' && shadow.alphaDestinationBlend === 'GR_BLEND_ONE_MINUS_SRC_ALPHA',
    'SOURCE_WORLD_SHADOW_MATERIAL: exact source alpha100, texture-times-alpha, blend, inherited cull and requested WBUFFER state required.');
    const names = ['psshould.wms', 'pslimb.wms', 'pstorso.wms', 'pshand.wms'];
    const paths = ['img3d/PSSHOULD.TGA', 'img3d/PSLIMB.TGA', 'img3d/PSTORSO.TGA', 'img3d/PSHAND.TGA'];
    const blobs = ['b05993dc691d1c6d7632c26fa37092d09ebba4d9', 'a589bfe2eb87dea0fff00421fdd81c25aad65ea4',
      'cbe1aebd1249da69cf6136e1072c20dc532901c2', 'e6f69144c2ff170d25afcc0398c0f2d68ed2c700'];
    requireValue(Array.isArray(shadow.textures) && shadow.textures.length === 4 &&
      shadow.textures.every((resource, slot) => resource.slot === slot && resource.sourceName === names[slot] &&
        resource.authoringPath === paths[slot] && resource.authoringGitBlob === blobs[slot] && typeof resource.missing === 'boolean' &&
        (resource.missing ? resource.texture === null :
          this.textures.has(resource.texture) &&
          /^[0-9a-f]{64}$/.test(resource.textureSha256) &&
          this.textures.get(resource.texture).sha256 === resource.textureSha256 &&
          this.textures.get(resource.texture).repeat === true && this.textures.get(resource.texture).linear === true &&
          this.textures.get(resource.texture).origin.includes(resource.authoringPath))) &&
      shadow.materialReady === shadow.textures.every(resource => !resource.missing) &&
      (shadow.materialReady || shadow.blocker?.startsWith('SOURCE_WORLD_SHADOW_TEXTURES_NOT_ADMITTED:')),
    'SOURCE_WORLD_SHADOW_RESOURCES: all four exact admitted original resources or explicit named missing-art status required; no substitute.');
    const vertices = packet.vertices, links = packet.links;
    requireValue(Array.isArray(vertices) && Array.isArray(links) && links.length % 4 === 0 &&
      links.length <= 14 * 10 * 4 && vertices.length === links.length / 4 * 30 &&
      vertices.every(value => Number.isFinite(Math.fround(value))) && integer(shadow.acceptedLinks) &&
      shadow.acceptedLinks === links.length / 4 && shadow.actors?.length === 14 &&
      shadow.actors.every((actor, i) => actor.id === i && integer(actor.rawFlags) &&
        actor.onScreen === Boolean(actor.rawFlags & 2) && integer(actor.acceptedLinks) && actor.acceptedLinks <= 10),
    'SOURCE_WORLD_SHADOW_SIZE: fourteen actors, at most ten source links each, six finite raster/ST vertices and four selectors per link required.');
    const seen = new Set(), counts = Array(14).fill(0), slots = [2, 0, 1, 3, 1, 3, 1, 1, 1, 1];
    for (let draw = 0; draw < links.length / 4; draw++) {
      const [actor, link, texture, bias] = links.slice(draw * 4, draw * 4 + 4), key = `${actor}/${link}`;
      requireValue(integer(actor) && actor < 14 && integer(link) && link < 10 &&
        texture === slots[link] && bias === 28 - 2 * link && shadow.actors[actor].onScreen && !seen.has(key),
      'SOURCE_WORLD_SHADOW_LINK: unique on-screen actor/source linkage, material selector and source bias28..10 required.');
      seen.add(key); counts[actor]++;
      const v = vertices.slice(draw * 30, draw * 30 + 30);
      requireValue([0, 1, 2].every(corner => v[corner * 5 + 2] > 0 && v[corner * 5 + 2] <= 1 &&
        v[corner * 5] >= -state.hres && v[corner * 5] < 2 * state.hres &&
        v[corner * 5 + 1] >= -state.vres && v[corner * 5 + 1] < 2 * state.vres) &&
        [[255, 255], [255, 0], [0, 0], [0, 0], [0, 255], [255, 255]]
          .every(([s, t], corner) => v[corner * 5 + 3] === s && v[corner * 5 + 4] === t) &&
        v.slice(10, 15).every((value, i) => value === v[15 + i]) &&
        v.slice(0, 5).every((value, i) => value === v[25 + i]),
      'SOURCE_WORLD_SHADOW_GEOMETRY: source A/B/C trivial acceptance and ABC/CDA raster/ST triangles required.');
    }
    requireValue(counts.every((count, actor) => count === shadow.actors[actor].acceptedLinks),
      'SOURCE_WORLD_SHADOW_COUNTS: each actual actor must account for its own accepted source links.');
    this.world = draws;
    const gl = this.gl;
    if (!this.worldBuffer) {
      this.worldBuffer = gl.createBuffer(); this.worldVao = gl.createVertexArray();
      gl.bindVertexArray(this.worldVao); gl.bindBuffer(gl.ARRAY_BUFFER, this.worldBuffer);
      gl.enableVertexAttribArray(0); gl.vertexAttribPointer(0, 3, gl.FLOAT, false, 20, 0);
      gl.enableVertexAttribArray(1); gl.vertexAttribPointer(1, 2, gl.FLOAT, false, 20, 12);
      this.shadow = program(gl, shadowVertex, shadowFragment);
      this.shadowUniforms = Object.fromEntries(['sourceProjection', 'rasterSize', 'normalizedDepthBias', 'atlas', 'cutoff', 'identityPass',
        'fogEnabled', 'fogColor', 'fogRange'].map(name => [name, gl.getUniformLocation(this.shadow, name)]));
      this.worldResourceCreations = (this.worldResourceCreations || 0) + 1;
    }
    const packed = Float32Array.from(vertices);
    gl.bindBuffer(gl.ARRAY_BUFFER, this.worldBuffer); gl.bufferData(gl.ARRAY_BUFFER, packed, gl.DYNAMIC_DRAW);
    gl.bindVertexArray(null);
    this.worldUploads = (this.worldUploads || 0) + 1;
    this.worldFx = { projection: Float32Array.from(projection), vertices: packed, links: Array.from(links), metadata: state };
  }

  fogUniforms(uniforms, diagnosticWithoutFog = false) {
    const gl = this.gl, state = this.worldFx?.metadata;
    gl.uniform1i(uniforms.fogEnabled, !diagnosticWithoutFog && state?.fog.enabled === true);
    if (!state) return;
    const fog = state.fog, color = fog.colorRgb;
    gl.uniform3f(uniforms.fogColor, ((color >>> 16) & 255) / 255, ((color >>> 8) & 255) / 255, (color & 255) / 255);
    gl.uniform2f(uniforms.fogRange, fog.start, fog.end);
    if (Object.hasOwn(uniforms, 'cameraDepthRow'))
      gl.uniform4fv(uniforms.cameraDepthRow, state.cameraDepthRow);
  }

  helmetPackets(styles) {
    const helmets = styles.looseHelmets || [], asset = styles.helmetAsset;
    requireValue(Array.isArray(helmets) && (styles.frame.enabled || helmets.length === 0),
      'Disabled source-fire cannot submit detached loose-helmet objects.');
    if (!helmets.length) return [];
    requireValue(asset?.mesh === 'original/loose-helmet/limb_phelm_obj' && this.meshes.get(asset.mesh)?.count === 267 &&
      asset.sourceSha256 === '03130e8e5dab04b4d5bd64290595b884bfb1e007440d9d228db9419f47614e5a' &&
      asset.geometrySha256 === 'd8c14967916b24b3135839680f1419c0af1a6aeceea34f3aa6a10d8127ff59c9' &&
      asset.origin === 'original-phelm-SDL-factor1.2-authoring-origin-not-native-helm.h' &&
      asset.uvOrigin === 'original-cvtmod-normalized-six-decimal-ST-ratio4-bottom-left' &&
      asset.sourceScale === 1.2 && asset.sourceTriangleOrder === 'Renderer321' &&
      asset.nativeGeneratedHeaderEquivalent === false,
    'Exact original PHELM authoring mesh/origin/UV required; no phead, football or generic helmet substitute.');
    requireValue(new Set(helmets.map(h => h.objectSerial)).size === helmets.length,
      'A detached helmet object must be submitted exactly once.');
    return helmets.map(h => {
      const texture = this.textures.get(h.texture), white = Boolean(styles.frame.globalBallFlags & 1);
      const packet = h.packet;
      requireValue(Number.isInteger(h.objectSerial) && h.objectSerial > 0 &&
        Number.isInteger(h.processSerial) && h.processSerial > 0 && Number.isInteger(h.actor) && h.actor >= 0 && h.actor < 14 &&
        h.sourceTick === styles.frame.sourceTick && h.entrySourceTick <= h.sourceTick && h.cacheSourceTick <= h.entrySourceTick &&
        h.objectId === 0x450 && h.drawOrder === 0x50000 && h.decalSlot === 2 && h.integerAspect === 4 &&
        h.textureWidth === 128 && h.textureHeight === 256 && texture?.width === 128 && texture?.height === 256 &&
        texture.sha256 === h.textureSha256 && h.texture.startsWith('teams/runtime/') &&
        h.alpha === 255 && h.scale === 2 && h.pitchRate === 40 && h.white === white && h.effectiveAlpha === 255 &&
        h.visible === !h.hidden && Number.isInteger(h.roll) && Number.isInteger(h.flightTime) &&
        Number.isInteger(h.bounces) && Number.isInteger(h.bounceTime) &&
        packet?.length === 20 && packet.every(Number.isFinite) && packet[0] === Number(!h.hidden) &&
        packet[1] === h.sourceTick && packet[2] === (styles.frame.globalBallFlags & 1) && packet[3] === h.alpha &&
        packet[7] === 0 && packet[11] === 0 && packet[15] === 0 && packet[19] === 1 &&
        h.worldMatrix?.length === 12 && h.worldMatrix.every((v, i) => Number.isFinite(Math.fround(v)) &&
          Math.fround(v) === Math.fround(packet[4 + (i % 4) * 4 + Math.floor(i / 4)])),
      'Loose-helmet GPU packet must bind same-tick detached source fields, captured team pdecal2/ratio4 and current GLOBAL ball WHITE.');
      return { metadata: h, matrix: Float32Array.from(packet.slice(4)) };
    });
  }

  present(camera, joints, models, appearances, ball, hudVertices, hudColors, hudTextures, hudCounts, status) {
    const start = performance.now();
    const snapshot = JSON.parse(status);
    requireValue(Boolean(snapshot.world) === Boolean(this.worldFx) &&
      (!snapshot.world || snapshot.world.sourceTick === snapshot.sourceTick && snapshot.world.epoch === snapshot.epoch &&
        JSON.stringify(snapshot.world) === JSON.stringify(this.worldFx.metadata)),
    'SOURCE_WORLD_PRESENT: the staged GPU packet must be the same actual source frame/epoch as Present.');
    requireValue(camera.length === 16 && joints.length === 14 * 15 * 16 && models.length === 14 &&
      appearances.length === 14 && this.world.length > 0 && this.ball, 'Incomplete actual kernel/render frame.');
    const trails = snapshot.ballTrails || [];
    requireValue(ball.length === 20 * (trails.length + 1) && ball.every(Number.isFinite) && (ball[0] === 0 || ball[0] === 1) &&
      ball[1] === snapshot.sourceTick && ball[1] === snapshot.ball.sourceTick &&
      ball[2] === snapshot.ball.rawFlags && ball[3] === 255 &&
      ball[7] === 0 && ball[11] === 0 && ball[15] === 0 && ball[19] === 1 &&
      Boolean(ball[0]) === snapshot.ball.visible &&
      snapshot.ball.worldMatrix.length === 12 &&
      snapshot.ball.worldMatrix.every((v, i) => Number.isFinite(Math.fround(v)) &&
        Math.fround(v) === Math.fround(ball[4 + (i % 4) * 4 + Math.floor(i / 4)])),
    'Free-ball GPU packet must bind the actual same-source-tick immutable rotation/scale/pivot matrix.');
    const ballTrails = trails.map((trail, index) => {
      const packet = Array.from(ball.slice(20 * (index + 1), 20 * (index + 2)));
      const expectedVisible = !trail.hidden && (snapshot.ball.carrier === -1 || Boolean(snapshot.ball.rawFlags & 0x800));
      requireValue(trail.sourceTick === snapshot.sourceTick && trail.entry?.sourceTick <= trail.sourceTick &&
        Number.isInteger(trail.objectSerial) && trail.objectSerial > 0 &&
        Number.isInteger(trail.processSerial) && trail.processSerial > 0 &&
        trail.objectId === 0x400 && trail.drawOrder === 0x50000 && trail.localFlags === 0x40 &&
        Number.isInteger(trail.stage) && trail.stage >= 0 && trail.stage < 5 &&
        trail.alpha === 215 - 15 * trail.stage && trail.scale === [2.25, 2.15, 2.05, 1.95, 1.85][trail.stage] &&
        trail.rawFlags === (0x40 | (snapshot.ball.rawFlags & 1)) &&
        trail.effectiveAlpha === (snapshot.ball.rawFlags & 1 ? 255 : trail.alpha) &&
        trail.pupBigBall === snapshot.ball.pupBigBall && trail.visible === expectedVisible &&
        trail.heading === trail.entry.heading && trail.elevation === trail.entry.elevation && trail.roll === trail.entry.roll &&
        trail.position.length === 3 && trail.position.every((v, i) => v === trail.entry.position[i]) &&
        packet[0] === Number(expectedVisible) && packet[1] === trail.sourceTick &&
        packet[2] === trail.rawFlags && packet[3] === trail.alpha &&
        packet[7] === 0 && packet[11] === 0 && packet[15] === 0 && packet[19] === 1 &&
        trail.worldMatrix?.length === 12 &&
        trail.worldMatrix.every((v, i) => Number.isFinite(Math.fround(v)) &&
          Math.fround(v) === Math.fround(packet[4 + (i % 4) * 4 + Math.floor(i / 4)])),
      'Football-trail GPU packet must retain its original entry copy, five stages, local ALT and current global draw gates.');
      return { metadata: trail, ball: packet, matrix: Float32Array.from(packet.slice(4)) };
    });
    requireValue(new Set(trails.map(t => t.objectSerial)).size === trails.length,
      'A source football-trail object must be submitted exactly once.');
    for (const helmet of this.fx?.helmets || [])
      requireValue(helmet.metadata.sourceTick === snapshot.sourceTick &&
        this.fx.metadata.globalBallFlags === snapshot.ball.rawFlags &&
        this.appearances.get(appearances[helmet.metadata.actor])?.textures[2] === helmet.metadata.texture,
      'Actual detached helmet must retain its selected actor team decal2 and current global ball flags, not a generic material.');
    this.current = {
      camera: Float32Array.from(camera), joints: Float32Array.from(joints), models: Array.from(models),
      appearances: Array.from(appearances), ball: Array.from(ball.slice(0, 20)), ballMatrix: Float32Array.from(ball.slice(4, 20)),
      ballTrails,
      hudVertices: Array.from(hudVertices),
      hudColors: Array.from(hudColors), hudTextures: Array.from(hudTextures), hudCounts: Array.from(hudCounts),
      teamSelectionPlayers: snapshot.teamSelectionPlayers == null ? null :
        SourceRenderer.prototype.teamSelectionFrame.call(this, snapshot.teamSelectionPlayers, snapshot, hudCounts),
    };
    this.drawScene(false);
    this.drawEffects(false);
    this.drawHud();
    this.assertHealthy('live C# source frame');
    this.frameCount++;
    this.lastMilliseconds = performance.now() - start;
    this.status = snapshot;
  }

  teamSelectionFrame(packet, snapshot, hudCounts) {
    const finite = value => Number.isFinite(value) && Number.isFinite(Math.fround(value));
    requireValue(packet.schema === 'source-tmsel-players/v1' && packet.epoch === snapshot.epoch &&
      snapshot.cabinetSession?.stage === 'Teams' && Number.isSafeInteger(packet.sourceTick) && packet.sourceTick > 0 &&
      Array.isArray(packet.projection) && packet.projection.length === 16 && packet.projection.every(finite) &&
      Array.isArray(packet.joints) && packet.joints.length === 2 * 15 * 16 && packet.joints.every(finite) &&
      Array.isArray(packet.models) && packet.models.length === 2 && packet.models[0] === 6 && packet.models[1] === 10 &&
      Array.isArray(packet.appearances) && packet.appearances.length === 2 &&
      packet.appearances.every(value => Number.isInteger(value) && value >= 0) &&
      Number.isInteger(packet.beforeHudPrimitive) && packet.beforeHudPrimitive >= 0 &&
      packet.beforeHudPrimitive <= hudCounts.length && Array.isArray(packet.actors) && packet.actors.length === 2,
    'SOURCE_TMSEL_PACKET: actual same-epoch Teams source process, two original498 models and complete native camera/poses required.');
    for (let side = 0; side < 2; side++) {
      const actor = packet.actors[side];
      requireValue(actor.id === (side ? 10 : 3) && actor.team === side && actor.rosterIndex === 3 &&
        Number.isInteger(actor.teamIndex) && actor.teamIndex >= 0 && actor.teamIndex < 31 &&
        actor.facing === (side ? 512 : 490) && Number.isInteger(actor.rawFlags) && actor.rawFlags >= 0 &&
        actor.script === (side ? 'qb_ready_anim' : 'qb_ready_f_anim') &&
        Number.isFinite(actor.sourceFrame) && actor.sourceFrame >= 0 && actor.sourceFrame < 77 &&
        Number.isSafeInteger(actor.processSerial) && actor.processSerial > 0 &&
        Number.isSafeInteger(actor.processBirthTick) && actor.processBirthTick > 0 &&
        actor.processBirthTick <= actor.lastWakeTick && actor.lastWakeTick === packet.sourceTick &&
        this.models.has(packet.models[side]) && this.appearances.has(packet.appearances[side]),
      'SOURCE_TMSEL_ACTOR: genuine original3/10 ready/flipped process wake, hovered team/static roster3 and real mesh/appearance required.');
    }
    return {
      ...packet, projection: Float32Array.from(packet.projection), joints: Float32Array.from(packet.joints),
      models: [...packet.models], appearances: [...packet.appearances], actors: structuredClone(packet.actors),
    };
  }

  drawTeamSelectionPlayers() {
    const frame = this.current.teamSelectionPlayers;
    if (!frame) return;
    const gl = this.gl;
    gl.useProgram(this.scene); gl.enable(gl.DEPTH_TEST); gl.depthMask(true);
    gl.clearDepth(1); gl.clear(gl.DEPTH_BUFFER_BIT);
    gl.enable(gl.CULL_FACE); gl.cullFace(gl.BACK); gl.frontFace(gl.CW);
    gl.disable(gl.BLEND); gl.disable(gl.POLYGON_OFFSET_FILL); gl.depthFunc(gl.LESS);
    gl.activeTexture(gl.TEXTURE0); gl.uniform1i(this.sceneUniforms.atlas, 0);
    gl.uniform1i(this.sceneUniforms.fogEnabled, false);
    gl.uniform1i(this.sceneUniforms.constantAlpha, false);
    gl.uniform1f(this.sceneUniforms.constantAlphaValue, 1);
    gl.uniform1i(this.sceneUniforms.textureTimesAlpha, false);
    gl.uniform1f(this.sceneUniforms.playerFlashAdd, 0);
    gl.uniformMatrix4fv(this.sceneUniforms.viewProjection, false, frame.projection);
    for (let side = 0; side < 2; side++) {
      const model = this.models.get(frame.models[side]), appearance = this.appearances.get(frame.appearances[side]);
      requireValue(model?.length === 15 && appearance,
        'SOURCE_TMSEL_DRAW: the real two selected model498 rigs/appearances must remain bound.');
      for (let joint = 0; joint < 15; joint++) {
        if (!model[joint].drawEnabled) continue;
        const matrix = frame.joints.subarray((side * 15 + joint) * 16, (side * 15 + joint + 1) * 16);
        for (const part of model[joint].parts) {
          gl.uniform1f(this.sceneUniforms.cutoff, part.slot === 0 ? -1 : 1 / 255);
          this.draw(part.mesh, appearance.textures[part.slot], matrix, null);
        }
      }
      const torso = frame.joints.subarray((side * 15 + appearance.numberMatrix) * 16,
        (side * 15 + appearance.numberMatrix + 1) * 16);
      gl.uniform1f(this.sceneUniforms.cutoff, 1 / 255);
      gl.enable(gl.POLYGON_OFFSET_FILL); gl.polygonOffset(0, -1);
      this.draw(appearance.numberMesh, appearance.numberTexture, torso, null);
      gl.disable(gl.POLYGON_OFFSET_FILL);
    }
  }

  blend(name) {
    const gl = this.gl;
    const values = {
      GR_BLEND_ONE: gl.ONE, GR_BLEND_ZERO: gl.ZERO,
      GR_BLEND_SRC_ALPHA: gl.SRC_ALPHA, GR_BLEND_ONE_MINUS_SRC_ALPHA: gl.ONE_MINUS_SRC_ALPHA,
    };
    requireValue(Object.hasOwn(values, name), `Unsupported original blend ${name}.`);
    return values[name];
  }

  draw(meshId, textureId, matrix, idColor) {
    const gl = this.gl, mesh = this.meshes.get(meshId), texture = this.textures.get(textureId);
    requireValue(mesh && texture, `Missing original draw binding ${meshId} / ${textureId}.`);
    gl.uniformMatrix4fv(this.sceneUniforms.model, false, matrix);
    gl.uniform4f(this.sceneUniforms.identityOutput, (idColor || 0) / 255, 0, 0, idColor === null ? 0 : 1);
    gl.bindTexture(gl.TEXTURE_2D, texture.texture);
    gl.bindVertexArray(mesh.vao);
    gl.drawElements(gl.TRIANGLES, mesh.count, gl.UNSIGNED_INT, 0);
    this.drawCalls++;
  }

  drawScene(identityPass, poppedHeadIdentityPass = false, observeMaterial = false,
    diagnosticWithoutFog = false, diagnosticWithoutShadows = false) {
    const gl = this.gl, frame = this.current;
    gl.useProgram(this.scene);
    SourceRenderer.prototype.fogUniforms.call(this, this.sceneUniforms, diagnosticWithoutFog);
    gl.enable(gl.DEPTH_TEST); gl.depthMask(true); gl.enable(gl.CULL_FACE); gl.cullFace(gl.BACK);
    // Raster X now uses the approved source geom.s negation, not a camera/cache reflection.
    gl.frontFace(gl.CW);
    gl.uniform1f(this.sceneUniforms.playerFlashAdd, 0);
    gl.uniform1f(this.sceneUniforms.constantAlphaValue, 1);
    gl.activeTexture(gl.TEXTURE0); gl.uniform1i(this.sceneUniforms.atlas, 0);
    gl.uniformMatrix4fv(this.sceneUniforms.viewProjection, false, frame.camera);
    gl.clearColor(0, 0, 0, 1); gl.clearDepth(1); gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
    this.drawCalls = 0;
    for (const draw of this.world.filter(draw => !this.worldFx || draw.state.sourceDrawOrder !== 0x48000)) {
      const state = draw.state;
      gl.depthFunc(state.depthFunction === 'GR_CMP_ALWAYS' ? gl.ALWAYS : gl.LESS);
      gl.uniform1f(this.sceneUniforms.cutoff, state.alphaFunction === 'GR_CMP_GREATER' ? state.alphaValue / 255 : -1);
      gl.uniform1i(this.sceneUniforms.constantAlpha, state.alphaSource === 'GR_ALPHASOURCE_CC_ALPHA');
      gl.uniform1i(this.sceneUniforms.textureTimesAlpha, state.colorFunction === 'GR_COLORCOMBINE_TEXTURE_TIMES_ALPHA');
      if (identityPass) gl.disable(gl.BLEND);
      else {
        gl.enable(gl.BLEND);
        gl.blendFuncSeparate(this.blend(state.rgbSource), this.blend(state.rgbDestination),
          this.blend(state.alphaSourceBlend), this.blend(state.alphaDestinationBlend));
      }
      if (state.depthBias) { gl.enable(gl.POLYGON_OFFSET_FILL); gl.polygonOffset(0, -state.depthBias); }
      else gl.disable(gl.POLYGON_OFFSET_FILL);
      this.draw(draw.mesh, draw.texture, identity, identityPass ? 0 : null);
    }
    if (!diagnosticWithoutShadows)
      SourceRenderer.prototype.drawShadows.call(this, identityPass, diagnosticWithoutFog);
    gl.useProgram(this.scene);
    gl.disable(gl.POLYGON_OFFSET_FILL); gl.disable(gl.BLEND); gl.depthFunc(gl.LESS);
    gl.uniform1f(this.sceneUniforms.cutoff, 1 / 255);
    gl.uniform1i(this.sceneUniforms.constantAlpha, false);
    gl.uniform1i(this.sceneUniforms.textureTimesAlpha, false);
    for (let actor = 0; actor < 14; actor++) {
      gl.uniform1f(this.sceneUniforms.playerFlashAdd, this.fx?.flashAlpha[actor] || 0);
      const model = this.models.get(frame.models[actor]), appearance = this.appearances.get(frame.appearances[actor]);
      requireValue(model && appearance, `Actor ${actor} has no real original appearance/pose.`);
      for (let joint = 0; joint < 15; joint++) {
        if (!model[joint].drawEnabled) continue;
        const parts = joint === appearance.headJoint && appearance.headParts ? appearance.headParts : model[joint].parts;
        const matrix = frame.joints.subarray((actor * 15 + joint) * 16, (actor * 15 + joint + 1) * 16);
        const identity = poppedHeadIdentityPass ?
          joint === appearance.headJoint && appearance.headVariant === 'original-phead-source-authored' ? actor + 1 : 0 : actor + 1;
        for (const part of parts) {
          // Source skin slot0 supplies opaque RGB even when ARMHND2's authored alpha is zero.
          // Keep the real uniform/facemask/number cutouts on every other slot.
          gl.uniform1f(this.sceneUniforms.cutoff, part.slot === 0 ? -1 : 1 / 255);
          this.draw(part.mesh, appearance.textures[part.slot], matrix, identityPass ? identity : null);
        }
      }
      const torso = frame.joints.subarray((actor * 15 + appearance.numberMatrix) * 16,
        (actor * 15 + appearance.numberMatrix + 1) * 16);
      gl.uniform1f(this.sceneUniforms.cutoff, 1 / 255);
      gl.enable(gl.POLYGON_OFFSET_FILL); gl.polygonOffset(0, -1);
      this.draw(appearance.numberMesh, appearance.numberTexture, torso, identityPass ? poppedHeadIdentityPass ? 0 : actor + 1 : null);
      gl.disable(gl.POLYGON_OFFSET_FILL);
    }
    if (this.worldFx) SourceRenderer.prototype.drawLateWorld.call(this, identityPass);
    this.lastBallDraw = null;
    this.lastBallTrailDraws = [];
    if (frame.ball[0]) {
      requireValue(frame.ballMatrix?.length === 16, 'Actual free-ball matrix is disconnected from the scene draw.');
      const matrix = frame.ballMatrix;
      const texture = frame.ball[2] & 0x40 ? this.ball.alternateTexture : this.ball.texture;
      // This one mesh retains source XYZ and explicit renderer321 indices.
      // Actors/world keep their existing reflected import winding and CW state.
      gl.frontFace(gl.CCW);
      gl.uniform1f(this.sceneUniforms.cutoff, -1);
      gl.uniform1i(this.sceneUniforms.constantAlpha, true);
      gl.uniform1i(this.sceneUniforms.textureTimesAlpha, true);
      gl.uniform1f(this.sceneUniforms.playerFlashAdd, frame.ball[2] & 1 ? 1 : 0);
      gl.uniform1f(this.sceneUniforms.constantAlphaValue, frame.ball[2] & 1 ? 1 : frame.ball[3] / 255);
      if (identityPass) gl.disable(gl.BLEND);
      else {
        gl.enable(gl.BLEND);
        gl.blendFuncSeparate(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA, gl.ONE, gl.ZERO);
      }
      this.draw(this.ball.mesh, texture, matrix, identityPass ? poppedHeadIdentityPass ? 0 : 200 : null);
      this.lastBallDraw = { sourceTick: frame.ball[1], mesh: this.ball.mesh, texture,
        textureSha256: this.textures.get(texture).sha256, matrix: Array.from(matrix),
        alpha: frame.ball[3], white: Boolean(frame.ball[2] & 1), frontFace: 'CCW-source321',
        indices: this.meshes.get(this.ball.mesh).count, metadata: this.ball.metadata };
      if (identityPass || observeMaterial)
        Object.assign(this.lastBallDraw, SourceRenderer.prototype.ballUniformProof.call(this, texture));
      gl.frontFace(gl.CW); gl.disable(gl.BLEND);
    }
    if (frame.ballTrails?.length)
      SourceRenderer.prototype.drawBallTrails.call(this, identityPass, poppedHeadIdentityPass, observeMaterial);
    SourceRenderer.prototype.drawLooseHelmets.call(this, identityPass, poppedHeadIdentityPass, observeMaterial);
    gl.bindVertexArray(null);
  }

  drawShadows(identityPass, diagnosticWithoutFog = false) {
    this.lastShadowDraws = [];
    const fx = this.worldFx;
    if (!fx?.metadata.shadows.materialReady || !fx.links.length) return;
    const gl = this.gl, material = fx.metadata.shadows;
    gl.useProgram(this.shadow);
    SourceRenderer.prototype.fogUniforms.call(this, this.shadowUniforms, diagnosticWithoutFog);
    gl.enable(gl.DEPTH_TEST); gl.depthFunc(gl.LESS); gl.depthMask(material.depthWrite);
    // Source raster quads are already projected; they do not have the imported mesh reflection.
    gl.enable(gl.CULL_FACE); gl.cullFace(gl.BACK); gl.frontFace(gl.CCW);
    gl.disable(gl.POLYGON_OFFSET_FILL);
    if (identityPass) gl.disable(gl.BLEND);
    else {
      gl.enable(gl.BLEND);
      gl.blendFuncSeparate(this.blend(material.rgbSource), this.blend(material.rgbDestination),
        this.blend(material.alphaSourceBlend), this.blend(material.alphaDestinationBlend));
    }
    gl.uniformMatrix4fv(this.shadowUniforms.sourceProjection, false, fx.projection);
    gl.uniform2f(this.shadowUniforms.rasterSize, fx.metadata.hres, fx.metadata.vres);
    gl.uniform1f(this.shadowUniforms.cutoff, material.alphaValue / 255);
    gl.uniform1i(this.shadowUniforms.identityPass, identityPass);
    gl.activeTexture(gl.TEXTURE0); gl.uniform1i(this.shadowUniforms.atlas, 0);
    gl.bindVertexArray(this.worldVao);
    for (let draw = 0; draw < fx.links.length / 4; draw++) {
      const [actor, link, texture, bias] = fx.links.slice(draw * 4, draw * 4 + 4);
      const id = material.textures[texture].texture;
      gl.bindTexture(gl.TEXTURE_2D, this.textures.get(id).texture);
      gl.uniform1f(this.shadowUniforms.normalizedDepthBias, bias / 65535);
      gl.drawArrays(gl.TRIANGLES, draw * 6, 6);
      this.drawCalls++;
      this.lastShadowDraws.push({ actor, link, texture: id, bias, sourceTick: fx.metadata.sourceTick,
        frontFace: 'CCW-source-raster', depthConversion: material.depthConversion });
    }
    gl.bindVertexArray(null); gl.disable(gl.POLYGON_OFFSET_FILL); gl.disable(gl.BLEND); gl.frontFace(gl.CW);
  }

  drawLateWorld(identityPass) {
    const gl = this.gl;
    this.lastLateWorldDraws = [];
    gl.uniform1f(this.sceneUniforms.playerFlashAdd, 0);
    gl.uniform1f(this.sceneUniforms.constantAlphaValue, 1);
    for (const draw of this.world.filter(draw => draw.state.sourceDrawOrder === 0x48000)) {
      const state = draw.state;
      gl.depthFunc(state.depthFunction === 'GR_CMP_ALWAYS' ? gl.ALWAYS : gl.LESS);
      gl.depthMask(state.depthMask === 'FXTRUE');
      gl.uniform1f(this.sceneUniforms.cutoff, state.alphaFunction === 'GR_CMP_GREATER' ? state.alphaValue / 255 : -1);
      gl.uniform1i(this.sceneUniforms.constantAlpha, state.alphaSource === 'GR_ALPHASOURCE_CC_ALPHA');
      gl.uniform1i(this.sceneUniforms.textureTimesAlpha, state.colorFunction === 'GR_COLORCOMBINE_TEXTURE_TIMES_ALPHA');
      if (identityPass) gl.disable(gl.BLEND);
      else {
        gl.enable(gl.BLEND);
        gl.blendFuncSeparate(this.blend(state.rgbSource), this.blend(state.rgbDestination),
          this.blend(state.alphaSourceBlend), this.blend(state.alphaDestinationBlend));
      }
      if (state.depthBias) { gl.enable(gl.POLYGON_OFFSET_FILL); gl.polygonOffset(0, -state.depthBias); }
      else gl.disable(gl.POLYGON_OFFSET_FILL);
      this.draw(draw.mesh, draw.texture, identity, identityPass ? 0 : null);
      this.lastLateWorldDraws.push({ mesh: draw.mesh, sourceDrawOrder: state.sourceDrawOrder,
        depthFunction: state.depthFunction, depthMask: state.depthMask });
    }
    gl.disable(gl.POLYGON_OFFSET_FILL); gl.disable(gl.BLEND); gl.depthFunc(gl.LESS); gl.depthMask(true);
  }

  ballUniformProof(texture) {
    const gl = this.gl, uniforms = this.sceneUniforms;
    return {
      gpuModel: Array.from(gl.getUniform(this.scene, uniforms.model)),
      gpuViewProjection: Array.from(gl.getUniform(this.scene, uniforms.viewProjection)),
      gpuConstantAlpha: gl.getUniform(this.scene, uniforms.constantAlpha),
      gpuConstantAlphaValue: gl.getUniform(this.scene, uniforms.constantAlphaValue),
      gpuTextureTimesAlpha: gl.getUniform(this.scene, uniforms.textureTimesAlpha),
      gpuWhiteAdd: gl.getUniform(this.scene, uniforms.playerFlashAdd),
      gpuOriginalTextureBound: gl.getParameter(gl.TEXTURE_BINDING_2D) === this.textures.get(texture).texture,
      gpuDepthTest: gl.isEnabled(gl.DEPTH_TEST),
      gpuDepthWrite: gl.getParameter(gl.DEPTH_WRITEMASK),
      gpuDepthFunction: gl.getParameter(gl.DEPTH_FUNC) === gl.LESS ? 'LESS' : gl.getParameter(gl.DEPTH_FUNC),
      gpuCullEnabled: gl.isEnabled(gl.CULL_FACE),
      gpuCullFace: gl.getParameter(gl.CULL_FACE_MODE) === gl.BACK ? 'BACK' : gl.getParameter(gl.CULL_FACE_MODE),
      gpuFrontFace: gl.getParameter(gl.FRONT_FACE) === gl.CCW ? 'CCW' : gl.getParameter(gl.FRONT_FACE),
      gpuBlendEnabled: gl.isEnabled(gl.BLEND),
      gpuSourceAlphaBlend: gl.getParameter(gl.BLEND_SRC_RGB) === gl.SRC_ALPHA &&
        gl.getParameter(gl.BLEND_DST_RGB) === gl.ONE_MINUS_SRC_ALPHA &&
        gl.getParameter(gl.BLEND_SRC_ALPHA) === gl.ONE && gl.getParameter(gl.BLEND_DST_ALPHA) === gl.ZERO,
    };
  }

  drawBallTrails(identityPass, poppedHeadIdentityPass, observeMaterial = false) {
    const gl = this.gl;
    gl.frontFace(gl.CCW);
    gl.uniform1f(this.sceneUniforms.cutoff, -1);
    gl.uniform1i(this.sceneUniforms.constantAlpha, true);
    gl.uniform1i(this.sceneUniforms.textureTimesAlpha, true);
    if (identityPass) gl.disable(gl.BLEND);
    else {
      gl.enable(gl.BLEND);
      gl.blendFuncSeparate(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA, gl.ONE, gl.ZERO);
    }
    for (const trail of this.current.ballTrails) {
      if (!trail.ball[0]) continue;
      const texture = this.ball.alternateTexture, white = Boolean(trail.ball[2] & 1);
      gl.uniform1f(this.sceneUniforms.constantAlphaValue, white ? 1 : trail.ball[3] / 255);
      gl.uniform1f(this.sceneUniforms.playerFlashAdd, white ? 1 : 0);
      this.draw(this.ball.mesh, texture, trail.matrix, identityPass ? poppedHeadIdentityPass ? 0 : 201 : null);
      const draw = { objectSerial: trail.metadata.objectSerial, processSerial: trail.metadata.processSerial,
        sourceTick: trail.ball[1], entrySourceTick: trail.metadata.entry.sourceTick, stage: trail.metadata.stage,
        mesh: this.ball.mesh, texture, textureSha256: this.textures.get(texture).sha256,
        matrix: Array.from(trail.matrix), alpha: trail.ball[3], effectiveAlpha: white ? 255 : trail.ball[3],
        white, frontFace: 'CCW-source321', indices: this.meshes.get(this.ball.mesh).count,
        rgbCombiner: white ? 'DECAL_TEXTURE_ADD_CCALPHA/FF' : 'TEXTURE_TIMES_ALPHA',
        alphaSource: 'CC_ALPHA', depthFunction: 'LESS', depthWrite: true, drawOrder: 0x50000 };
      if (identityPass || observeMaterial)
        Object.assign(draw, SourceRenderer.prototype.ballUniformProof.call(this, texture));
      this.lastBallTrailDraws.push(draw);
    }
    gl.frontFace(gl.CW); gl.disable(gl.BLEND);
  }

  drawLooseHelmets(identityPass, poppedHeadIdentityPass, observeMaterial = false) {
    this.lastHelmetDraws = [];
    const helmets = this.fx?.helmets || [];
    if (!helmets.length) return;
    const gl = this.gl;
    gl.enable(gl.DEPTH_TEST); gl.depthFunc(gl.LESS); gl.depthMask(true);
    gl.enable(gl.CULL_FACE); gl.cullFace(gl.BACK); gl.frontFace(gl.CCW);
    gl.uniform1f(this.sceneUniforms.cutoff, -1);
    gl.uniform1i(this.sceneUniforms.constantAlpha, true);
    gl.uniform1i(this.sceneUniforms.textureTimesAlpha, true);
    if (identityPass) gl.disable(gl.BLEND);
    else {
      gl.enable(gl.BLEND);
      gl.blendFuncSeparate(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA, gl.ONE, gl.ZERO);
    }
    for (const helmet of helmets) {
      const h = helmet.metadata;
      if (!h.visible) continue;
      const white = Boolean(this.fx.metadata.globalBallFlags & 1);
      gl.uniform1f(this.sceneUniforms.constantAlphaValue, white ? 1 : h.alpha / 255);
      gl.uniform1f(this.sceneUniforms.playerFlashAdd, white ? 1 : 0);
      this.draw(this.fx.helmetAsset.mesh, h.texture, helmet.matrix,
        identityPass ? poppedHeadIdentityPass ? 0 : 202 : null);
      const draw = { objectSerial: h.objectSerial, processSerial: h.processSerial, actor: h.actor,
        sourceTick: h.sourceTick, entrySourceTick: h.entrySourceTick, mesh: this.fx.helmetAsset.mesh,
        geometrySha256: this.fx.helmetAsset.geometrySha256, texture: h.texture, textureSha256: this.textures.get(h.texture).sha256,
        matrix: Array.from(helmet.matrix), alpha: h.alpha, white, globalBallFlags: this.fx.metadata.globalBallFlags,
        decalSlot: 2, integerAspect: 4, indices: this.meshes.get(this.fx.helmetAsset.mesh).count,
        frontFace: 'CCW-source321', rgbCombiner: white ? 'DECAL_TEXTURE_ADD_CCALPHA/FF' : 'TEXTURE_TIMES_ALPHA',
        alphaSource: 'CC_ALPHA', depthFunction: 'LESS', depthWrite: true, drawOrder: 0x50000 };
      if (identityPass || observeMaterial)
        Object.assign(draw, SourceRenderer.prototype.ballUniformProof.call(this, h.texture));
      this.lastHelmetDraws.push(draw);
    }
    gl.frontFace(gl.CW); gl.disable(gl.BLEND);
  }

  drawEffects(proofMask, dustOnly = false) {
    const fx = this.fx, gl = this.gl;
    if (!fx?.metadata.enabled || fx.textures.length === 0) return;
    gl.useProgram(this.fire); gl.enable(gl.DEPTH_TEST); gl.depthFunc(gl.ALWAYS); gl.depthMask(true);
    SourceRenderer.prototype.fogUniforms.call(this, this.fireUniforms);
    gl.disable(gl.POLYGON_OFFSET_FILL);
    if (fx.metadata.cull === 0) gl.disable(gl.CULL_FACE);
    else { gl.enable(gl.CULL_FACE); gl.cullFace(fx.metadata.cull === 1 ? gl.FRONT : gl.BACK); }
    gl.enable(gl.BLEND);
    gl.blendFuncSeparate(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA, gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
    gl.uniformMatrix4fv(this.fireUniforms.sourceProjection, false, fx.projection);
    gl.uniform1i(this.fireUniforms.proofMask, proofMask); gl.uniform1i(this.fireUniforms.atlas, 0);
    gl.bindVertexArray(this.fireVao); gl.bindBuffer(gl.ARRAY_BUFFER, this.fireBuffer);
    gl.bufferData(gl.ARRAY_BUFFER, fx.vertices, gl.DYNAMIC_DRAW);
    for (let i = 0; i < fx.textures.length; i++) {
      if (dustOnly && fx.textures[i] < 17) continue;
      gl.bindTexture(gl.TEXTURE_2D, this.textures.get(`fire/${fx.textures[i]}`).texture);
      gl.drawArrays(gl.TRIANGLES, i * 6, 6);
    }
    gl.bindVertexArray(null); gl.disable(gl.BLEND); gl.depthFunc(gl.LESS);
  }

  drawHud() {
    const gl = this.gl, frame = this.current;
    requireValue(frame.hudCounts.length === frame.hudColors.length && frame.hudCounts.length === frame.hudTextures.length &&
      frame.hudCounts.reduce((a, b) => a + b, 0) * 4 === frame.hudVertices.length,
    'Actual original HUD draw-command inventory is incomplete.');
    requireValue(!this.fx || [...this.fx.hudHighlight].every(i => i < frame.hudCounts.length),
      'Source meter highlight referenced a missing authored HUD primitive.');
    const rect = this.canvas.getBoundingClientRect();
    const hudState = () => {
      gl.useProgram(this.hud); gl.disable(gl.DEPTH_TEST); gl.depthMask(false); gl.disable(gl.CULL_FACE);
      gl.enable(gl.BLEND); gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
      gl.uniform2f(this.hudUniforms.contentSize, rect.width, rect.height);
      gl.uniform1i(this.hudUniforms.atlas, 0);
      gl.bindVertexArray(this.hudVao); gl.bindBuffer(gl.ARRAY_BUFFER, this.hudBuffer);
    };
    const vertices = new Float32Array(frame.hudVertices.length * 2);
    let first = 0;
    for (let primitive = 0; primitive < frame.hudCounts.length; primitive++) {
      const color = frame.hudColors[primitive] >>> 0;
      for (let i = 0; i < frame.hudCounts[primitive]; i++) {
        const v = first + i;
        vertices.set(frame.hudVertices.slice(v * 4, v * 4 + 4), v * 8);
        vertices[v * 8 + 4] = ((color >>> 16) & 255) / 255;
        vertices[v * 8 + 5] = ((color >>> 8) & 255) / 255;
        vertices[v * 8 + 6] = (color & 255) / 255;
        vertices[v * 8 + 7] = (color >>> 24) / 255;
      }
      first += frame.hudCounts[primitive];
    }
    hudState();
    gl.bufferData(gl.ARRAY_BUFFER, vertices, gl.DYNAMIC_DRAW);
    const boundary = frame.teamSelectionPlayers?.beforeHudPrimitive;
    const teamPlayers = () => {
      SourceRenderer.prototype.drawTeamSelectionPlayers.call(this);
      hudState();
    };
    first = 0;
    for (let primitive = 0; primitive < frame.hudCounts.length;) {
      if (primitive === boundary) teamPlayers();
      const textureId = frame.hudTextures[primitive];
      const highlight = this.fx?.hudHighlight.has(primitive) === true;
      let count = 0;
      do { count += frame.hudCounts[primitive++]; } while (primitive < frame.hudCounts.length &&
        primitive !== boundary &&
        frame.hudTextures[primitive] === textureId && (this.fx?.hudHighlight.has(primitive) === true) === highlight);
      const texture = this.textures.get(textureId);
      requireValue(texture, `Actual authored HUD atlas is missing: ${textureId}.`);
      gl.uniform1f(this.hudUniforms.highlightAdd, highlight ? 192 / 255 : 0);
      gl.bindTexture(gl.TEXTURE_2D, texture.texture); gl.drawArrays(gl.TRIANGLES, first, count);
      first += count;
    }
    if (boundary === frame.hudCounts.length) teamPlayers();
    gl.bindVertexArray(null); gl.depthMask(true); gl.disable(gl.BLEND);
  }

  resize() {
    const rect = this.canvas.getBoundingClientRect(), dpr = globalThis.devicePixelRatio || 1;
    const width = Math.max(1, Math.round(rect.width * dpr)), height = Math.max(1, Math.round(rect.height * dpr));
    if (this.canvas.width !== width || this.canvas.height !== height) {
      this.canvas.width = width; this.canvas.height = height;
    }
    this.gl.viewport(0, 0, width, height);
    return { width, height, cssWidth: rect.width, cssHeight: rect.height, dpr };
  }

  proof() {
    requireValue(this.current && this.status, 'No actual live source frame exists for GPU evidence.');
    const gl = this.gl, width = 512, height = 384;
    const framebuffer = gl.createFramebuffer(), color = gl.createTexture(), depth = gl.createRenderbuffer();
    gl.bindTexture(gl.TEXTURE_2D, color);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, width, height, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
    gl.bindRenderbuffer(gl.RENDERBUFFER, depth); gl.renderbufferStorage(gl.RENDERBUFFER, gl.DEPTH_COMPONENT24, width, height);
    gl.bindFramebuffer(gl.FRAMEBUFFER, framebuffer);
    gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, color, 0);
    gl.framebufferRenderbuffer(gl.FRAMEBUFFER, gl.DEPTH_ATTACHMENT, gl.RENDERBUFFER, depth);
    try {
      requireValue(gl.checkFramebufferStatus(gl.FRAMEBUFFER) === gl.FRAMEBUFFER_COMPLETE,
        'Actual WebGL geometry-identity proof framebuffer failed.');
      gl.viewport(0, 0, width, height);
      this.drawScene(false, false, true);
      const cameraUniform = Array.from(gl.getUniform(this.scene, this.sceneUniforms.viewProjection));
      let worldProof = null;
      if (this.worldFx) {
        const fx = this.worldFx, uploaded = new Float32Array(fx.vertices.length);
        gl.bindBuffer(gl.ARRAY_BUFFER, this.worldBuffer);
        gl.getBufferSubData(gl.ARRAY_BUFFER, 0, uploaded);
        worldProof = {
          metadata: fx.metadata, sourceShadowDraws: this.lastShadowDraws,
          gpuBufferBytes: gl.getBufferParameter(gl.ARRAY_BUFFER, gl.BUFFER_SIZE),
          gpuBufferMatchesActualPacket: uploaded.every((value, i) => value === fx.vertices[i]),
          firstActualRasterVertices: Array.from(uploaded.slice(0, 30)), firstActualLinkSelectors: fx.links.slice(0, 16),
          gpuShadowShaderLinked: gl.getProgramParameter(this.shadow, gl.LINK_STATUS),
          resourceCreations: this.worldResourceCreations, uploads: this.worldUploads,
          fogEnabled: gl.getUniform(this.scene, this.sceneUniforms.fogEnabled),
          fogColor: Array.from(gl.getUniform(this.scene, this.sceneUniforms.fogColor)),
          fogRange: Array.from(gl.getUniform(this.scene, this.sceneUniforms.fogRange)),
          cameraDepthRow: Array.from(gl.getUniform(this.scene, this.sceneUniforms.cameraDepthRow)),
          lateWorldDraws: this.lastLateWorldDraws,
          qualification: fx.metadata.shadows.materialReady ?
            'Actual current source-linked material draws; portable depth/fog arithmetic, not native pixels.' :
            'Actual current source-linked C# packet/GPU buffer only. Four original textures are not admitted: zero shadow material draws/pixels, no substitute. Unbound fog remains off.',
        };
      }
      const proofViewport = Array.from(gl.getParameter(gl.VIEWPORT));
      const ballTrailMaterialDraws = this.lastBallTrailDraws;
      const looseHelmetMaterialDraws = this.lastHelmetDraws;
      const materialPixels = new Uint8Array(width * height * 4);
      gl.readPixels(0, 0, width, height, gl.RGBA, gl.UNSIGNED_BYTE, materialPixels);
      let withoutShadows = null;
      if (worldProof && this.worldFx.metadata.shadows.materialReady) {
        this.drawScene(false, false, false, false, true);
        withoutShadows = new Uint8Array(width * height * 4);
        gl.readPixels(0, 0, width, height, gl.RGBA, gl.UNSIGNED_BYTE, withoutShadows);
      }
      if (worldProof && this.worldFx.metadata.fog.enabled) {
        this.drawScene(false, false, false, true);
        const withoutFog = new Uint8Array(width * height * 4);
        gl.readPixels(0, 0, width, height, gl.RGBA, gl.UNSIGNED_BYTE, withoutFog);
        let changedPixels = 0, absoluteRgbDifference = 0;
        const samples = [];
        for (let i = 0; i < materialPixels.length; i += 4) {
          let difference = 0;
          for (let channel = 0; channel < 3; channel++)
            difference += Math.abs(materialPixels[i + channel] - withoutFog[i + channel]);
          if (difference) {
            changedPixels++; absoluteRgbDifference += difference;
            if (samples.length < 5) samples.push({
              pixel: i / 4, suppliedFog: Array.from(materialPixels.slice(i, i + 3)),
              diagnosticWithoutFog: Array.from(withoutFog.slice(i, i + 3)),
            });
          }
        }
        worldProof.fogPixels = { changedPixels, absoluteRgbDifference, samples,
          method: 'Actual supplied user-fog material pass versus an explicitly labelled no-fog diagnostic using the SAME current source geometry/camera/depth. Neither diagnostic replaces the gameplay screenshot or changes C# state.' };
        this.assertHealthy('actual user-fog material GPU differential');
      }
      this.drawScene(true);
      const ballDraw = this.lastBallDraw;
      const ballTrailDraws = this.lastBallTrailDraws;
      const looseHelmetDraws = this.lastHelmetDraws;
      const pixels = new Uint8Array(width * height * 4), actorPixels = Array(14).fill(0);
      let ballPixels = 0, ballTrailPixels = 0, ballTrailColoredPixels = 0, ballTrailNonWhitePixels = 0, shadowPixels = 0;
      let looseHelmetPixels = 0, looseHelmetColoredPixels = 0, looseHelmetNonWhitePixels = 0;
      const ballTrailColorBounds = { min: [255, 255, 255], max: [0, 0, 0] };
      gl.readPixels(0, 0, width, height, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
      let shadowMaterialPixels = 0;
      for (let i = 0; i < pixels.length; i += 4) {
        if (pixels[i + 1] !== 0 || pixels[i + 2] !== 0) continue;
        if (pixels[i] >= 1 && pixels[i] <= 14) actorPixels[pixels[i] - 1]++;
        if (pixels[i] === 200) ballPixels++;
        if (pixels[i] === 190) {
          shadowPixels++;
          if (withoutShadows && [0, 1, 2].some(channel =>
            materialPixels[i + channel] !== withoutShadows[i + channel])) shadowMaterialPixels++;
        }
        if (pixels[i] === 202) {
          looseHelmetPixels++;
          if (materialPixels[i] || materialPixels[i + 1] || materialPixels[i + 2]) looseHelmetColoredPixels++;
          if (materialPixels[i] !== 255 || materialPixels[i + 1] !== 255 || materialPixels[i + 2] !== 255)
            looseHelmetNonWhitePixels++;
        }
        if (worldProof) worldProof.shadowMaterialPixels = shadowMaterialPixels;
        if (pixels[i] === 201) {
          ballTrailPixels++;
          if (materialPixels[i] || materialPixels[i + 1] || materialPixels[i + 2]) ballTrailColoredPixels++;
          if (materialPixels[i] !== 255 || materialPixels[i + 1] !== 255 || materialPixels[i + 2] !== 255)
            ballTrailNonWhitePixels++;
          for (let channel = 0; channel < 3; channel++) {
            ballTrailColorBounds.min[channel] = Math.min(ballTrailColorBounds.min[channel], materialPixels[i + channel]);
            ballTrailColorBounds.max[channel] = Math.max(ballTrailColorBounds.max[channel], materialPixels[i + channel]);
          }
        }
      }
      this.assertHealthy('actual original geometry GPU identity pass');
      const poppedHeadPixels = Array(14).fill(0);
      if (this.current.appearances.some(id => this.appearances.get(id).headVariant === 'original-phead-source-authored')) {
        this.drawScene(true, true);
        gl.readPixels(0, 0, width, height, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
        for (let i = 0; i < pixels.length; i += 4)
          if (pixels[i + 1] === 0 && pixels[i + 2] === 0 && pixels[i] >= 1 && pixels[i] <= 14)
            poppedHeadPixels[pixels[i] - 1]++;
        this.assertHealthy('actual original popped-head-only geometry GPU identity pass');
      }
      let firePixels = 0, dustPixels = 0;
      if (this.fx?.metadata.enabled) {
        gl.clearColor(0, 0, 0, 0); gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
        this.drawEffects(true);
        gl.readPixels(0, 0, width, height, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
        for (let i = 0; i < pixels.length; i += 4) if (pixels[i] || pixels[i + 1] || pixels[i + 2]) firePixels++;
        this.assertHealthy('actual original fire geometry/alpha/frame GPU pass');
        if (this.fx.metadata.dustDraws > 0) {
          gl.clearColor(0, 0, 0, 0); gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
          this.drawEffects(true, true);
          gl.readPixels(0, 0, width, height, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
          for (let i = 0; i < pixels.length; i += 4) if (pixels[i] || pixels[i + 1] || pixels[i + 2]) dustPixels++;
          this.assertHealthy('same actual original DIRT objects/textures/alpha dust-only GPU identity pass');
        }
      }
      return {
        sourceTick: this.status.sourceTick, width, height, actorPixels, ballPixels, ballOrigin: this.ball.origin,
        cameraUniform, cameraPacket: Array.from(this.current.camera), proofViewport,
        contentViewport: [0, 0, this.canvas.width, this.canvas.height],
        ballDraw, ballTrailDraws, ballTrailPixels, ballTrailMaterialDraws, ballTrailColoredPixels,
        ballTrailNonWhitePixels, ballTrailColorBounds,
        looseHelmetDraws, looseHelmetMaterialDraws, looseHelmetPixels, looseHelmetColoredPixels, looseHelmetNonWhitePixels,
        visibleHome: actorPixels.slice(0, 7).filter(n => n > 0).length,
        visibleAway: actorPixels.slice(7).filter(n => n > 0).length,
        method: 'Separate normal-material and identity GPU passes using the SAME current original meshes/textures/poses/projection/depth. Trail color samples use the matching identity mask; neither pass is a substituted gameplay screenshot.',
        device: this.device, meshUploads: this.meshUploads, worldDraws: this.world.length,
        appearances: this.current.appearances.map(id => this.appearances.get(id)),
        textureCount: this.textures.size, frameCount: this.frameCount,
        firePixels, dustPixels, fireFrame: this.fx?.metadata,
        poppedHeadPixels,
        poppedHeadBindings: this.current.appearances.map((id, actor) => ({ actor, ...this.appearances.get(id) }))
          .filter(binding => binding.headVariant === 'original-phead-source-authored'),
        actorFlash: this.fx?.flashAlpha, actorRawFlags: this.fx?.rawFlags,
        meterHighlightPrimitives: this.fx ? Array.from(this.fx.hudHighlight) : [],
        meterCombiner: this.fx?.hudStyle.hudCombiner, meterConstantColor: this.fx?.hudStyle.hudConstantColor,
        world: worldProof, shadowPixels,
      };
    } finally {
      gl.bindFramebuffer(gl.FRAMEBUFFER, null);
      gl.deleteFramebuffer(framebuffer); gl.deleteTexture(color); gl.deleteRenderbuffer(depth);
      gl.viewport(0, 0, this.canvas.width, this.canvas.height);
      this.drawScene(false); this.drawEffects(false); this.drawHud();
    }
  }
}
