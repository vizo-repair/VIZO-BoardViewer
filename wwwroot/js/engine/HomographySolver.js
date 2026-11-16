// ============================================================================
// FILE: wwwroot/js/engine/HomographySolver.js (VIZO 2.0)
// Projective 8-DOF solver (DLT) for camera perspective correction
// ============================================================================

export function computeHomography4Points(srcPoints, dstPoints) {
    if (!srcPoints || !dstPoints || srcPoints.length < 4 || dstPoints.length < 4) {
        return computeAffine3Points(srcPoints, dstPoints);
    }

    // Build 8x8 linear system for DLT (Direct Linear Transformation)
    const A = [];
    const B = [];

    for (let i = 0; i < 4; i++) {
        const x = srcPoints[i].x;
        const y = srcPoints[i].y;
        const u = dstPoints[i].x;
        const v = dstPoints[i].y;

        A.push([x, y, 1, 0, 0, 0, -u * x, -u * y]);
        B.push(u);

        A.push([0, 0, 0, x, y, 1, -v * x, -v * y]);
        B.push(v);
    }

    const h = solveGauss8x8(A, B);
    if (!h) return computeAffine3Points(srcPoints, dstPoints);

    return [
        h[0], h[1], h[2],
        h[3], h[4], h[5],
        h[6], h[7], 1.0
    ];
}

// Solve 8x8 linear system using Gaussian elimination with partial pivoting
function solveGauss8x8(A, B) {
    const n = 8;
    for (let i = 0; i < n; i++) {
        let maxEl = Math.abs(A[i][i]);
        let maxRow = i;
        for (let k = i + 1; k < n; k++) {
            if (Math.abs(A[k][i]) > maxEl) {
                maxEl = Math.abs(A[k][i]);
                maxRow = k;
            }
        }

        if (maxEl < 1e-12) return null;

        for (let k = i; k < n; k++) {
            const tmp = A[maxRow][k];
            A[maxRow][k] = A[i][k];
            A[i][k] = tmp;
        }
        const tmpB = B[maxRow];
        B[maxRow] = B[i];
        B[i] = tmpB;

        for (let k = i + 1; k < n; k++) {
            const c = -A[k][i] / A[i][i];
            for (let j = i; j < n; j++) {
                if (i === j) A[k][j] = 0;
                else A[k][j] += c * A[i][j];
            }
            B[k] += c * B[i];
        }
    }

    const x = new Array(n).fill(0);
    for (let i = n - 1; i >= 0; i--) {
        let sum = 0;
        for (let j = i + 1; j < n; j++) {
            sum += A[i][j] * x[j];
        }
        x[i] = (B[i] - sum) / A[i][i];
    }
    return x;
}

function computeAffine3Points(srcPoints, dstPoints) {
    if (!srcPoints || !dstPoints || srcPoints.length < 3 || dstPoints.length < 3) return null;
    const n = Math.min(srcPoints.length, dstPoints.length);

    let sumXX = 0, sumYY = 0, sumXY = 0, sumX = 0, sumY = 0;
    let sumUX = 0, sumUY = 0, sumU = 0, sumVX = 0, sumVY = 0, sumV = 0;

    for (let i = 0; i < n; i++) {
        const x = srcPoints[i].x, y = srcPoints[i].y;
        const u = dstPoints[i].x, v = dstPoints[i].y;
        sumXX += x * x; sumYY += y * y; sumXY += x * y;
        sumX += x; sumY += y;
        sumUX += u * x; sumUY += u * y; sumU += u;
        sumVX += v * x; sumVY += v * y; sumV += v;
    }

    const A = [
        [sumXX, sumXY, sumX],
        [sumXY, sumYY, sumY],
        [sumX, sumY, n]
    ];

    const detA = A[0][0] * (A[1][1] * A[2][2] - A[1][2] * A[2][1]) -
        A[0][1] * (A[1][0] * A[2][2] - A[1][2] * A[2][0]) +
        A[0][2] * (A[1][0] * A[2][1] - A[1][1] * A[2][0]);

    if (Math.abs(detA) < 1e-14) return null;

    const abc = solve3x3(A, [sumUX, sumUY, sumU], detA);
    const def = solve3x3(A, [sumVX, sumVY, sumV], detA);
    if (!abc || !def) return null;

    return [
        abc[0], abc[1], abc[2],
        def[0], def[1], def[2],
        0.0, 0.0, 1.0
    ];
}

function solve3x3(A, b, detA) {
    const invA = [
        (A[1][1] * A[2][2] - A[1][2] * A[2][1]) / detA,
        (A[0][2] * A[2][1] - A[0][1] * A[2][2]) / detA,
        (A[0][1] * A[1][2] - A[0][2] * A[1][1]) / detA,
        (A[1][2] * A[2][0] - A[1][0] * A[2][2]) / detA,
        (A[0][0] * A[2][2] - A[0][2] * A[2][0]) / detA,
        (A[0][2] * A[1][0] - A[0][0] * A[1][2]) / detA,
        (A[1][0] * A[2][1] - A[1][1] * A[2][0]) / detA,
        (A[0][1] * A[2][0] - A[0][0] * A[2][1]) / detA,
        (A[0][0] * A[1][1] - A[0][1] * A[1][0]) / detA
    ];

    return [
        invA[0] * b[0] + invA[1] * b[1] + invA[2] * b[2],
        invA[3] * b[0] + invA[4] * b[1] + invA[5] * b[2],
        invA[6] * b[0] + invA[7] * b[1] + invA[8] * b[2]
    ];
}

export function transformPoint(matrix, x, y) {
    if (!matrix || matrix.length !== 9) return { x, y };
    const w = matrix[6] * x + matrix[7] * y + matrix[8];
    if (Math.abs(w) < 1e-7) return { x: 0, y: 0 };
    return {
        x: (matrix[0] * x + matrix[1] * y + matrix[2]) / w,
        y: (matrix[3] * x + matrix[4] * y + matrix[5]) / w
    };
}

export function resolvePinCadCoords(netlist, rawIdentifier) {
    if (!netlist?.components || !rawIdentifier) return { refDes: '', pinNum: '1', cadX: 0, cadY: 0, side: 'TOP', label: rawIdentifier || '' };

    let clean = String(rawIdentifier).trim().split(' ')[0].split('(')[0];
    let refDes = clean.toUpperCase();
    let pinNum = '1';

    if (clean.includes('.')) {
        const parts = clean.split('.');
        refDes = parts[0].toUpperCase();
        pinNum = parts[1] || '1';
    }

    const comp = netlist.components[refDes] || Object.entries(netlist.components).find(([k]) => k.equalsIgnoreCase(refDes))?.[1];
    if (!comp) return { refDes, pinNum, cadX: 0, cadY: 0, side: 'TOP', label: rawIdentifier };

    const side = (comp.side || 'TOP').toUpperCase().startsWith('B') ? 'BOTTOM' : 'TOP';

    if (comp.pins && typeof comp.pins === 'object') {
        const matchedKey = Object.keys(comp.pins).find(k => k.equalsIgnoreCase(pinNum)) || Object.keys(comp.pins)[0];
        if (matchedKey && comp.pins[matchedKey]) {
            return { refDes, pinNum: matchedKey, cadX: comp.pins[matchedKey].x, cadY: comp.pins[matchedKey].y, side, label: `${refDes}.${matchedKey}` };
        }
    }

    return { refDes, pinNum: '1', cadX: comp.x, cadY: comp.y, side, label: refDes };
}