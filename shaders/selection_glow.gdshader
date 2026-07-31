shader_type canvas_item;
// 铁幕突袭 — 选中发光轮廓着色器
// 效果：选中时在精灵边缘绘制发光轮廓

uniform vec4 glow_color : source_color = vec4(0.3, 0.85, 1.0, 1.0);
uniform float glow_intensity : hint_range(0.0, 3.0) = 1.5;
uniform float glow_width : hint_range(0.0, 8.0) = 2.0;

void fragment() {
    vec4 col = texture(TEXTURE, UV);

    // 采样周围像素检测边缘
    vec2 size = 1.0 / SIZE;
    float alpha = 0.0;
    for (float x = -glow_width; x <= glow_width; x += 1.0) {
        for (float y = -glow_width; y <= glow_width; y += 1.0) {
            vec2 offset = vec2(x, y) * size;
            float a = texture(TEXTURE, UV + offset).a;
            alpha = max(alpha, a);
        }
    }

    // 原始精灵alpha
    float origAlpha = col.a;

    // 轮廓 = 周围有alpha但当前无alpha的区域
    float outline = clamp(alpha - origAlpha, 0.0, 1.0) * glow_intensity;

    // 混合轮廓颜色
    vec3 glowRgb = glow_color.rgb * outline;
    col.rgb = mix(col.rgb, glowRgb, outline * glow_color.a);
    col.a = max(col.a, outline * glow_color.a);

    COLOR = col;
}
